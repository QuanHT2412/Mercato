-- ============================================================================
-- MERCATO — Schema v2, database-per-service
-- Ba database vật lý riêng: identity_db, catalog_db, ordering_db.
-- KHÔNG có FK xuyên service: user_id/variant_id tại ranh giới là UUID trần,
-- integrity đảm bảo bằng application logic + events (ADR: cross-service integrity).
-- Lưu ý: đây là bản tham chiếu thiết kế — implement thật đi qua EF Core
-- code-first migration cho từng service, không chạy file này trực tiếp.
-- ============================================================================


-- ============================================================================
-- DATABASE 1: identity_db  (Identity Service)
-- ============================================================================
-- \c identity_db

CREATE TABLE users (
    id            UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    email         VARCHAR(255) NOT NULL,
    username      VARCHAR(50)  NOT NULL UNIQUE,
    password_hash TEXT         NOT NULL,           -- ASP.NET Core Identity hasher
    name          VARCHAR(100) NOT NULL,
    avatar_url    TEXT,
    is_active     BOOLEAN      NOT NULL DEFAULT TRUE,
    is_verified   BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX idx_users_email ON users (email);

-- RBAC tối giản theo brief: 2 role, phân quyền qua claims trong JWT.
CREATE TABLE roles (
    id   UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(50) NOT NULL UNIQUE            -- 'Customer' | 'Admin'
);

CREATE TABLE user_roles (
    user_id     UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role_id     UUID        NOT NULL REFERENCES roles(id) ON DELETE CASCADE,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, role_id)
);

-- Refresh token theo RFC 9700: LƯU HASH (không plaintext), rotation theo family.
-- Reuse detection: token đã revoked/replaced mà xuất hiện lại -> revoke cả family_id.
CREATE TABLE refresh_tokens (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash  TEXT        NOT NULL UNIQUE,      -- SHA-256 của token
    family_id   UUID        NOT NULL,             -- chuỗi rotation cùng 1 phiên đăng nhập
    replaced_by UUID,                             -- token kế nhiệm trong family (NULL = mới nhất)
    device_info TEXT,
    ip_address  INET,
    is_revoked  BOOLEAN     NOT NULL DEFAULT FALSE,
    expires_at  TIMESTAMPTZ NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_refresh_tokens_family ON refresh_tokens (family_id);
CREATE INDEX idx_refresh_tokens_active
    ON refresh_tokens (user_id, expires_at)
    WHERE is_revoked = FALSE;

-- Outbox: Identity publish UserRegistered.
CREATE TABLE outbox_messages (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type   VARCHAR(100) NOT NULL,
    payload      JSONB        NOT NULL,
    occurred_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    processed_at TIMESTAMPTZ                       -- NULL = chưa dispatch
);

CREATE INDEX idx_outbox_unprocessed
    ON outbox_messages (occurred_at)
    WHERE processed_at IS NULL;


-- ============================================================================
-- DATABASE 2: catalog_db  (Catalog Service)
-- ============================================================================
-- \c catalog_db

CREATE TABLE categories (
    id         UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    parent_id  UUID         REFERENCES categories(id) ON DELETE SET NULL,
    name       VARCHAR(100) NOT NULL,
    slug       VARCHAR(100) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_categories_parent_id ON categories (parent_id);

CREATE TABLE products (
    id          UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    category_id UUID         REFERENCES categories(id) ON DELETE SET NULL,
    name        VARCHAR(255) NOT NULL,
    description TEXT,
    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    -- Postgres FTS theo brief (tsvector sinh tự động, GIN index):
    search_vector tsvector GENERATED ALWAYS AS (
        setweight(to_tsvector('simple', coalesce(name, '')), 'A') ||
        setweight(to_tsvector('simple', coalesce(description, '')), 'B')
    ) STORED
);

CREATE INDEX idx_products_category_id ON products (category_id);
CREATE INDEX idx_products_active ON products (is_active) WHERE is_active = TRUE;
CREATE INDEX idx_products_search ON products USING GIN (search_vector);

CREATE TABLE product_variants (
    id         UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID          NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    sku        VARCHAR(100)  NOT NULL UNIQUE,
    name       VARCHAR(255)  NOT NULL,
    price      NUMERIC(14,0) NOT NULL CHECK (price >= 0),   -- VND: không thập phân
    stock      INT           NOT NULL DEFAULT 0 CHECK (stock >= 0),
    image_url  TEXT,
    is_active  BOOLEAN       NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_variants_product_id ON product_variants (product_id);

-- Stock reservation: giữ chỗ trong payment window (gRPC CheckAndReserveStock).
-- Available thực = stock - SUM(active reservations). Hangfire/consumer release khi
-- OrderCancelled/OrderExpired; convert thành trừ stock thật khi OrderConfirmed.
-- order_id là UUID TRẦN (thuộc ordering_db) — không FK xuyên service.
CREATE TABLE stock_reservations (
    id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    variant_id UUID        NOT NULL REFERENCES product_variants(id) ON DELETE CASCADE,
    order_id   UUID        NOT NULL,              -- ranh giới service: không FK
    quantity   INT         NOT NULL CHECK (quantity > 0),
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (order_id, variant_id)
);

CREATE INDEX idx_reservations_variant ON stock_reservations (variant_id);
CREATE INDEX idx_reservations_expiry  ON stock_reservations (expires_at);

-- Outbox: Catalog có thể publish ProductPriceChanged... (v1 có thể chưa dùng, giữ sẵn khung)
CREATE TABLE outbox_messages (
    id           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type   VARCHAR(100) NOT NULL,
    payload      JSONB        NOT NULL,
    occurred_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    processed_at TIMESTAMPTZ
);

CREATE INDEX idx_outbox_unprocessed
    ON outbox_messages (occurred_at)
    WHERE processed_at IS NULL;


-- ============================================================================
-- DATABASE 3: ordering_db  (Ordering Service)
-- ============================================================================
-- \c ordering_db

-- Lifecycle đúng brief: Pending -> AwaitingPayment -> Confirmed | Cancelled | Expired
CREATE TYPE order_status AS ENUM (
    'pending', 'awaiting_payment', 'confirmed', 'cancelled', 'expired'
);

CREATE TYPE payment_tx_status AS ENUM ('pending', 'success', 'failed', 'refunded');

-- Cart thuộc Ordering theo brief. user_id/variant_id: UUID trần, không FK.
-- Luật giỏ (max 20 line, qty 1-10) enforce ở Domain layer; CHECK làm lưới an toàn.
CREATE TABLE cart_items (
    id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID        NOT NULL,
    variant_id UUID        NOT NULL,
    quantity   INT         NOT NULL DEFAULT 1 CHECK (quantity BETWEEN 1 AND 10),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (user_id, variant_id)
);

CREATE INDEX idx_cart_items_user_id ON cart_items (user_id);

CREATE TABLE orders (
    id               UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id          UUID          NOT NULL,      -- ranh giới service: không FK
    status           order_status  NOT NULL DEFAULT 'pending',
    total_amount     NUMERIC(14,0) NOT NULL CHECK (total_amount >= 0),
    receiver         VARCHAR(100)  NOT NULL,      -- snapshot địa chỉ giao hàng
    phone            VARCHAR(20)   NOT NULL,
    address          TEXT          NOT NULL,
    note             TEXT,
    payment_deadline TIMESTAMPTZ,                 -- set khi vào awaiting_payment;
                                                  -- Hangfire quét để expire
    created_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_orders_user_id ON orders (user_id);
CREATE INDEX idx_orders_created_at ON orders (created_at DESC);
CREATE INDEX idx_orders_expiry_scan
    ON orders (payment_deadline)
    WHERE status = 'awaiting_payment';            -- partial index cho expiry job

CREATE TABLE order_items (
    id           UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id     UUID          NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    variant_id   UUID          NOT NULL,          -- ranh giới service: không FK
    product_name VARCHAR(255)  NOT NULL,          -- snapshot: giữ nguyên tư duy cũ, đúng
    variant_name VARCHAR(255)  NOT NULL,
    quantity     INT           NOT NULL CHECK (quantity > 0),
    unit_price   NUMERIC(14,0) NOT NULL CHECK (unit_price >= 0),
    subtotal     NUMERIC(14,0) NOT NULL CHECK (subtotal >= 0),
    created_at   TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_order_items_order_id ON order_items (order_id);

-- Mock payment provider phía sau interface; bảng tx vẫn model như thật.
CREATE TABLE payment_transactions (
    id                UUID              PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id          UUID              NOT NULL REFERENCES orders(id) ON DELETE RESTRICT,
    method_code       VARCHAR(50)       NOT NULL DEFAULT 'mock_vnpay',  -- thay bảng payment_methods
    status            payment_tx_status NOT NULL DEFAULT 'pending',
    amount            NUMERIC(14,0)     NOT NULL CHECK (amount > 0),
    provider_tx_id    TEXT,
    provider_response JSONB,
    paid_at           TIMESTAMPTZ,
    created_at        TIMESTAMPTZ       NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ       NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_payment_tx_order_id ON payment_transactions (order_id);

-- Outbox: BẮT BUỘC theo brief — OrderCreated/OrderConfirmed/OrderCancelled/OrderExpired
-- ghi cùng transaction với thay đổi trạng thái order; dispatcher relay sang RabbitMQ.
CREATE TABLE outbox_messages (
    id           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type   VARCHAR(100) NOT NULL,
    payload      JSONB        NOT NULL,
    occurred_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    processed_at TIMESTAMPTZ
);

CREATE INDEX idx_outbox_unprocessed
    ON outbox_messages (occurred_at)
    WHERE processed_at IS NULL;

-- Inbox (idempotency cho consumer): chống xử lý trùng khi RabbitMQ redeliver.
-- Wolverine có hỗ trợ sẵn — bảng này minh họa khái niệm nếu tự implement.
CREATE TABLE inbox_messages (
    message_id   UUID        PRIMARY KEY,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ============================================================================
-- ĐÃ CẮT so với v1 (có chủ đích, khớp brief — ứng viên phase 2):
--   social.reviews                      -> phase 2 (brief section 7)
--   shop.stores + store_id khắp nơi     -> multi-vendor ngoài scope
--   ordering.wallets + wallet_tx        -> hệ ví ngoài scope, mock payment là đủ
--   auth.oauth_accounts                 -> OAuth ngoài scope
--   auth.permissions/role_permissions   -> RBAC 2 role qua claims là đủ
--   auth.blacklisted_tokens             -> access token ngắn hạn + rotation thay thế
--   shop.customer_profiles/addresses    -> địa chỉ snapshot thẳng vào order (v1);
--                                          address book là tính năng phase 2
--   ordering.payment_methods            -> 1 mock provider, cột method_code là đủ
-- ============================================================================

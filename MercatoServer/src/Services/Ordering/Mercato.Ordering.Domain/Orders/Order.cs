using Mercato.Ordering.Domain.Exceptions;

namespace Mercato.Ordering.Domain.Orders;

// Where the order goes, snapshotted onto the order. Can only be obtained through Create,
// so holding a ShippingDetails means it has already been validated — checkout relies on
// that to reject a bad address BEFORE it reserves any stock.
public sealed record ShippingDetails
{
    private ShippingDetails(string receiver, string phone, string address, string? note)
    {
        Receiver = receiver;
        Phone = phone;
        Address = address;
        Note = note;
    }

    public string Receiver { get; }

    public string Phone { get; }

    public string Address { get; }

    public string? Note { get; }

    public static ShippingDetails Create(string? receiver, string? phone, string? address, string? note) =>
        new(
            Required(receiver, "Receiver", 100),
            Required(phone, "Phone", 20),
            Required(address, "Address", 500),
            string.IsNullOrWhiteSpace(note) ? null : note.Trim());

    private static string Required(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{field} must not be empty.");
        }

        value = value.Trim();

        if (value.Length > maxLength)
        {
            throw new DomainValidationException($"{field} must not exceed {maxLength} characters.");
        }

        return value;
    }
}

// Aggregate root. Every status change goes through a method here, so the lifecycle rules
// live in one place and cannot be bypassed by a handler setting a property.
public sealed class Order
{
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(15);

    private readonly List<OrderItem> _items = [];

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }

    // VND, decimal, computed once here from the snapshotted lines — the frontend only
    // ever displays it (CLAUDE.md section 6).
    public decimal TotalAmount { get; private set; }
    public string Receiver { get; private set; }
    public string Phone { get; private set; }
    public string Address { get; private set; }
    public string? Note { get; private set; }

    // Set when the order starts waiting for payment; null before that.
    public DateTimeOffset? PaymentDeadline { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    // EF Core materialization only — real instances are created via Place.
    private Order()
    {
        Receiver = null!;
        Phone = null!;
        Address = null!;
    }

    private Order(Guid id, Guid userId, ShippingDetails shipping, DateTimeOffset now)
    {
        Id = id;
        UserId = userId;
        Status = OrderStatus.Pending;
        Receiver = shipping.Receiver;
        Phone = shipping.Phone;
        Address = shipping.Address;
        Note = shipping.Note;
        CreatedAt = now;
        UpdatedAt = now;
    }

    // The id exists before the order does: checkout needs it to reserve stock in Catalog
    // first, and only then creates the order from what was actually reserved.
    public static Guid NewId() => Guid.CreateVersion7();

    public static Order Place(Guid id, Guid userId, ShippingDetails shipping, IReadOnlyCollection<OrderLine> lines, DateTimeOffset now)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainValidationException("An order needs an id and a user.");
        }

        if (lines is null || lines.Count == 0)
        {
            throw new DomainValidationException("An order needs at least one item.");
        }

        if (shipping is null)
        {
            throw new DomainValidationException("Shipping details are required.");
        }

        var order = new Order(id, userId, shipping, now);

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new DomainValidationException("Order item quantity must be positive.");
            }

            if (line.UnitPrice < 0)
            {
                throw new DomainValidationException("Order item price must not be negative.");
            }

            order._items.Add(new OrderItem(id, line, now));
        }

        order.TotalAmount = order._items.Sum(i => i.Subtotal);

        return order;
    }

    // Pending -> AwaitingPayment. Starts the 15-minute payment window.
    public void AwaitPayment(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOrderTransitionException(Id, Status, "sent to payment");
        }

        Status = OrderStatus.AwaitingPayment;
        PaymentDeadline = now + PaymentWindow;
        UpdatedAt = now;
    }

    public bool IsPaymentOverdue(DateTimeOffset now) =>
        Status == OrderStatus.AwaitingPayment && PaymentDeadline is { } deadline && now >= deadline;

    // Whether a payment attempt may start at all — checked BEFORE money moves.
    public void EnsurePayable(DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderTransitionException(Id, Status, "paid");
        }

        // The expiry job only runs once a minute; the deadline itself is the rule, so an
        // overdue order is unpayable even if the job has not marked it Expired yet.
        if (IsPaymentOverdue(now))
        {
            throw new InvalidOrderTransitionException(Id, Status, "paid after its payment deadline");
        }
    }

    // AwaitingPayment -> Confirmed.
    public void Confirm(DateTimeOffset now)
    {
        EnsurePayable(now);

        Status = OrderStatus.Confirmed;
        UpdatedAt = now;
    }

    // Pending | AwaitingPayment -> Cancelled. A paid order cannot be cancelled here
    // (refunds are out of scope for v1).
    public void Cancel(DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.AwaitingPayment))
        {
            throw new InvalidOrderTransitionException(Id, Status, "cancelled");
        }

        Status = OrderStatus.Cancelled;
        UpdatedAt = now;
    }

    // AwaitingPayment -> Expired, only once the deadline has really passed.
    public void Expire(DateTimeOffset now)
    {
        if (!IsPaymentOverdue(now))
        {
            throw new InvalidOrderTransitionException(Id, Status, "expired");
        }

        Status = OrderStatus.Expired;
        UpdatedAt = now;
    }
}

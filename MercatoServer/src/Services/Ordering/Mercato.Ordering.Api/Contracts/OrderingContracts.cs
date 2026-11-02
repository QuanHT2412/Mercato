namespace Mercato.Ordering.Api.Contracts;

// Request bodies only; responses are the Application result records (CartResult,
// OrderResult, OrderSummary).
//
// Note what is NOT here: no user id (it comes from the access token, never from the
// body) and no price or total anywhere (they come from Catalog at checkout).

public sealed record SetCartItemRequest(int Quantity);

public sealed record CheckoutRequest(string? Receiver, string? Phone, string? Address, string? Note);

// PaymentToken stands in for what a real gateway would hand back to the client. With the
// mock provider: omit it (approved) or send "tok_decline" (declined).
public sealed record PayOrderRequest(string? PaymentToken);

namespace Mercato.Ordering.Domain.Orders;

// Pending -> AwaitingPayment -> Confirmed
//                           \-> Cancelled | Expired
// Confirmed, Cancelled and Expired are terminal: a cancelled/expired order is never
// reactivated, the customer places a new one (CLAUDE.md section 6).
public enum OrderStatus
{
    Pending = 0,
    AwaitingPayment = 1,
    Confirmed = 2,
    Cancelled = 3,
    Expired = 4,
}

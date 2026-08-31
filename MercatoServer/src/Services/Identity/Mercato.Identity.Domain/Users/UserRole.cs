namespace Mercato.Identity.Domain.Users;

// Deliberately a single role per user (one column) instead of the roles/user_roles
// tables from the reference schema: with exactly two roles and no per-role data, a join
// table buys nothing. The role travels as a claim in the access token.
public enum UserRole
{
    Customer = 0,
    Admin = 1,
}

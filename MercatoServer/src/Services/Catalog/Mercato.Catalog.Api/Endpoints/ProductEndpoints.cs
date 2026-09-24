using System.Security.Claims;
using Mercato.Catalog.Api.Contracts;
using Mercato.Catalog.Application.Products;

namespace Mercato.Catalog.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products").WithTags("Products");

        // Browse is public. A caller who happens to be an Admin additionally sees
        // deactivated products (and may ask for them in the list via includeInactive).
        group.MapGet("/", SearchAsync)
            .WithName("SearchProducts")
            .WithSummary("Browse / search active products, paged")
            .Produces<PagedResult<ProductSummary>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetProduct")
            .WithSummary("Product detail with its variants")
            .Produces<ProductDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var admin = group.MapGroup(string.Empty).RequireAuthorization(JwtValidationExtensions.AdminPolicy);

        admin.MapPost("/", CreateAsync)
            .WithName("CreateProduct")
            .WithSummary("Create a product with its variants (Admin)")
            .Produces<ProductDetails>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateProduct")
            .WithSummary("Update name, description, category and active flag (Admin)")
            .Produces<ProductDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapDelete("/{id:guid}", DeactivateAsync)
            .WithName("DeleteProduct")
            .WithSummary("Take a product off sale — a soft delete, existing orders keep their snapshot (Admin)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPost("/{id:guid}/variants", AddVariantAsync)
            .WithName("AddVariant")
            .WithSummary("Add a variant to a product (Admin)")
            .Produces<ProductDetails>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPut("/{id:guid}/variants/{variantId:guid}", UpdateVariantAsync)
            .WithName("UpdateVariant")
            .WithSummary("Update a variant's name, price, stock and active flag (Admin)")
            .Produces<ProductDetails>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [AsParameters] ProductListRequest request,
        ClaimsPrincipal user,
        SearchProductsHandler handler,
        CancellationToken cancellationToken)
    {
        var query = new ProductSearchQuery(
            request.Search,
            request.CategoryId,
            request.Page ?? 1,
            request.PageSize ?? SearchProductsHandler.DefaultPageSize,
            // The flag is honoured only for admins; for everyone else it is silently
            // ignored rather than rejected, so a public client cannot probe with it.
            IncludeInactive: request.IncludeInactive == true && IsAdmin(user));

        return Results.Ok(await handler.HandleAsync(query, cancellationToken));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        GetProductHandler handler,
        CancellationToken cancellationToken)
    {
        var product = await handler.HandleAsync(id, includeInactive: IsAdmin(user), cancellationToken);

        return product is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: $"Product '{id}' was not found.")
            : Results.Ok(product);
    }

    private static async Task<IResult> CreateAsync(
        CreateProductRequest request,
        CreateProductHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateProductCommand(
            request.Name,
            request.Description,
            request.CategoryId,
            [.. (request.Variants ?? []).Select(v => new NewVariant(v.Sku, v.Name, v.Price, v.Stock, v.ImageUrl))]);

        var product = await handler.HandleAsync(command, cancellationToken);

        return Results.Created($"/products/{product.Id}", product);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        UpdateProductHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateProductCommand(id, request.Name, request.Description, request.CategoryId, request.IsActive);

        return Results.Ok(await handler.HandleAsync(command, cancellationToken));
    }

    private static async Task<IResult> DeactivateAsync(Guid id, DeactivateProductHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> AddVariantAsync(
        Guid id,
        VariantRequest request,
        AddVariantHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new AddVariantCommand(id, request.Sku, request.Name, request.Price, request.Stock, request.ImageUrl);
        var product = await handler.HandleAsync(command, cancellationToken);

        return Results.Created($"/products/{product.Id}", product);
    }

    private static async Task<IResult> UpdateVariantAsync(
        Guid id,
        Guid variantId,
        UpdateVariantRequest request,
        UpdateVariantHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateVariantCommand(
            id, variantId, request.Name, request.Price, request.Stock, request.ImageUrl, request.IsActive);

        return Results.Ok(await handler.HandleAsync(command, cancellationToken));
    }

    private static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin");
}

using Mercato.Catalog.Api.Contracts;
using Mercato.Catalog.Application.Categories;

namespace Mercato.Catalog.Api.Endpoints;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categories");

        group.MapGet("/", ListAsync)
            .WithName("ListCategories")
            .WithSummary("All categories (flat list; build the tree from parentId)")
            .Produces<IReadOnlyList<CategoryResult>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(JwtValidationExtensions.AdminPolicy)
            .WithName("CreateCategory")
            .WithSummary("Create a category (Admin)")
            .Produces<CategoryResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(JwtValidationExtensions.AdminPolicy)
            .WithName("DeleteCategory")
            .WithSummary("Delete a category; its products and children become uncategorised (Admin)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(ListCategoriesHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(cancellationToken));

    private static async Task<IResult> CreateAsync(
        CreateCategoryRequest request,
        CreateCategoryHandler handler,
        CancellationToken cancellationToken)
    {
        var category = await handler.HandleAsync(new CreateCategoryCommand(request.Name, request.ParentId), cancellationToken);

        return Results.Created($"/categories/{category.Id}", category);
    }

    private static async Task<IResult> DeleteAsync(Guid id, DeleteCategoryHandler handler, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(id, cancellationToken);

        return Results.NoContent();
    }
}

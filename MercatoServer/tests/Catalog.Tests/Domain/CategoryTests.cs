using Mercato.Catalog.Domain.Categories;
using Mercato.Catalog.Domain.Exceptions;

namespace Catalog.Tests.Domain;

public class CategoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Laptop", "laptop")]
    [InlineData("  Điện thoại & Phụ kiện  ", "dien-thoai-phu-kien")]
    [InlineData("Đồng hồ thông minh", "dong-ho-thong-minh")]
    [InlineData("TV 4K -- OLED", "tv-4k-oled")]
    public void Slug_From_StripsDiacriticsAndCollapsesSeparators(string name, string expected)
    {
        Assert.Equal(expected, Slug.From(name));
    }

    [Fact]
    public void Create_TrimsTheName_AndDerivesTheSlug()
    {
        var parentId = Guid.CreateVersion7();

        var category = Category.Create("  Bàn phím cơ ", parentId, Now);

        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal("Bàn phím cơ", category.Name);
        Assert.Equal("ban-phim-co", category.Slug);
        Assert.Equal(parentId, category.ParentId);
        Assert.Equal(Now, category.CreatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("&&&")]
    public void Create_WithANameThatYieldsNoSlug_Throws(string name)
    {
        Assert.Throws<DomainValidationException>(() => Category.Create(name, null, Now));
    }

    [Fact]
    public void Create_WithNameTooLong_Throws()
    {
        Assert.Throws<DomainValidationException>(() => Category.Create(new string('a', 101), null, Now));
    }
}

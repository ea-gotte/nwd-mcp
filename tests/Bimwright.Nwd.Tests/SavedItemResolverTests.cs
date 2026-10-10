using Bimwright.Nwd.Shared.Infrastructure;
namespace Bimwright.Nwd.Tests;

public sealed class SavedItemResolverTests
{
    private sealed class Item(string name, params Item[] children)
    {
        public string Name = name;
        public Guid Guid = Guid.NewGuid();
        public Item[] Children = children;
    }
    private static Item? Resolve(Item[] roots, string q)
        => SavedItemResolver.Resolve(roots, q, x => x.Name, x => x.Children, x => x.Guid);

    [Fact]
    public void Literal_slash_and_nested_folder_paths_round_trip()
    {
        var literal = new Item("external 3/4");
        var leaf = new Item("a~/b");
        Item[] roots = [literal, new Item("Folder/A", leaf)];
        Assert.Same(literal, Resolve(roots, "external 3/4"));
        Assert.Same(literal, Resolve(roots, "path:/external 3~14"));
        Assert.Same(leaf, Resolve(roots, "path:/Folder~1A/a~0~1b"));
        Assert.Same(leaf, Resolve(roots, SavedItemResolver.Id(leaf.Guid)));
    }
    [Fact]
    public void Ambiguous_literal_and_hierarchy_never_choose_first_match()
    {
        var literal = new Item("A/B");
        var nested = new Item("B");
        Item[] roots = [literal, new Item("A", nested)];
        Assert.Throws<InvalidOperationException>(() => Resolve(roots, "A/B"));
        Assert.Same(literal, Resolve(roots, "path:/A~1B"));
        Assert.Same(nested, Resolve(roots, "path:/A/B"));
    }
    [Fact]
    public void Duplicate_names_need_id_and_missing_identifier_stays_missing()
    {
        var a = new Item("same"); var b = new Item("same");
        Item[] roots = [a, b];
        Assert.Throws<InvalidOperationException>(() => Resolve(roots, "same"));
        Assert.Throws<InvalidOperationException>(() => Resolve(roots, "path:/same"));
        Assert.Same(b, Resolve(roots, SavedItemResolver.Id(b.Guid)));
        Assert.Null(Resolve(roots, "absent"));
        Assert.Null(Resolve(roots, "id:invalid"));
    }
    [Fact]
    public void SDK_items_without_GUID_use_escaped_path_identifier()
    {
        var a = new Item("one/~") { Guid = Guid.Empty };
        var b = new Item("two/~") { Guid = Guid.Empty };
        Item[] roots = [a, b];
        var id = SavedItemResolver.Id(b.Guid, "/two~1~0");
        Assert.Equal("path:/two~1~0", id);
        Assert.Same(b, Resolve(roots, id));
    }

    [Fact]
    public void Legacy_unambiguous_paths_and_case_insensitive_names_still_work()
    {
        var leaf = new Item("View2");
        Item[] roots = [new Item("Animation1", leaf)];
        Assert.Same(leaf, Resolve(roots, "Animation1/View2"));
        Assert.Same(leaf, Resolve(roots, "view2"));
    }
}

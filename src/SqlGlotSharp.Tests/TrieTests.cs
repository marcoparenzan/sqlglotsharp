namespace SqlGlotSharp.Tests;

public class TrieTests
{
    [Fact]
    public void Lookup_distinguishes_prefix_exists_and_failed_and_returns_stop_node()
    {
        var root = trie.new_trie(new[] { "cat", "car" });
        var prefix = trie.in_trie(root, "ca");
        Assert.Equal(trie.TrieResult.PREFIX, prefix.result);
        Assert.True(prefix.subtrie.ContainsKey("t"));
        var exists = trie.in_trie(root, "cat");
        Assert.Equal(trie.TrieResult.EXISTS, exists.result);
        Assert.Equal(true, exists.subtrie[0]);
        var failed = trie.in_trie(root, "cab");
        Assert.Equal(trie.TrieResult.FAILED, failed.result);
        Assert.Same(prefix.subtrie, failed.subtrie);
        Assert.Same(root, trie.in_trie(root, "dog").subtrie);
    }

    [Fact]
    public void Existing_trie_is_mutated_and_empty_key_still_fails_lookup()
    {
        var root = trie.new_trie(new[] { "a", "" });
        Assert.Same(root, trie.new_trie(new[] { "ab" }, root));
        Assert.Equal(trie.TrieResult.EXISTS, trie.in_trie(root, "a").result);
        Assert.Equal(trie.TrieResult.EXISTS, trie.in_trie(root, "ab").result);
        Assert.True(root.ContainsKey(0));
        Assert.Equal(trie.TrieResult.FAILED, trie.in_trie(root, "").result);
    }

    [Fact]
    public void Trie_supports_unicode_code_points_and_arbitrary_hashable_keys()
    {
        var root = trie.new_trie(new[] { "😀x" });
        Assert.True(root.ContainsKey("😀"));
        Assert.Equal(trie.TrieResult.PREFIX, trie.in_trie(root, "😀").result);
        var sequenceRoot = trie.new_trie(new[] { new object[] { 1, "x" } });
        Assert.Equal(trie.TrieResult.EXISTS, trie.in_trie(sequenceRoot, new object[] { 1, "x" }).result);
        Assert.Equal(trie.TrieResult.FAILED, trie.in_trie(sequenceRoot, new object[] { 2 }).result);
    }
}

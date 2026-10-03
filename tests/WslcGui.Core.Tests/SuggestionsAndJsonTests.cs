namespace WslcGui.Core.Tests;

public class SuggestionsTests
{
    private static readonly string[] Images = ["postgres:16-alpine", "redis:7-alpine", "alpine:latest", "debian:bookworm-slim"];

    [Fact]
    public void Prefix_matches_come_before_contains_matches() =>
        Assert.Equal(["alpine:latest", "postgres:16-alpine", "redis:7-alpine"], Suggestions.Filter(Images, "alp"));

    [Fact]
    public void Matching_ignores_case_and_surrounding_spaces() =>
        Assert.Equal(["redis:7-alpine"], Suggestions.Filter(Images, "  REDIS"));

    [Fact]
    public void Empty_text_lists_everything_up_to_the_limit() =>
        Assert.Equal(Images.Take(2), Suggestions.Filter(Images, "", max: 2));

    [Fact]
    public void Exact_match_and_duplicates_are_left_out() =>
        Assert.Empty(Suggestions.Filter(["alpine:latest", "alpine:latest"], "alpine:latest"));

    [Theory]
    [InlineData(@"C:\data", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData("./data", true)]
    [InlineData("/mnt/data", true)]
    [InlineData("c:", true)]
    [InlineData("pgdata", false)]
    [InlineData("my-volume_1", false)]
    [InlineData("", false)]
    public void Recognizes_host_paths(string text, bool expected) => Assert.Equal(expected, Suggestions.LooksLikePath(text));
}

public class JsonTokenizerTests
{
    [Fact]
    public void Tokenizes_a_property_line()
    {
        var tokens = JsonTokenizer.TokenizeLine("    \"Image\": \"alpine:latest\",");

        Assert.Equal(
            [
                new JsonToken(4, 7, JsonTokenKind.PropertyName),
                new JsonToken(11, 1, JsonTokenKind.Punctuation),
                new JsonToken(13, 15, JsonTokenKind.StringValue),
                new JsonToken(28, 1, JsonTokenKind.Punctuation),
            ],
            tokens);
    }

    [Fact]
    public void Tokenizes_numbers_keywords_and_brackets()
    {
        var tokens = JsonTokenizer.TokenizeLine("\"a\" : [-1.5e3, true, null, false]");

        Assert.Equal(
            [JsonTokenKind.PropertyName, JsonTokenKind.Punctuation, JsonTokenKind.Punctuation, JsonTokenKind.Number, JsonTokenKind.Punctuation,
                JsonTokenKind.Keyword, JsonTokenKind.Punctuation, JsonTokenKind.Keyword, JsonTokenKind.Punctuation, JsonTokenKind.Keyword, JsonTokenKind.Punctuation],
            tokens.Select(t => t.Kind));
        Assert.Equal(new JsonToken(7, 6, JsonTokenKind.Number), tokens[3]);
    }

    [Fact]
    public void Escaped_quotes_stay_inside_the_string()
    {
        var tokens = JsonTokenizer.TokenizeLine("\"cmd\": \"echo \\\"hi\\\"\"");

        Assert.Equal(new JsonToken(7, 13, JsonTokenKind.StringValue), tokens[2]);
    }

    [Fact]
    public void Unterminated_string_and_unknown_words_do_not_throw()
    {
        Assert.Single(JsonTokenizer.TokenizeLine("\"open"));
        Assert.Empty(JsonTokenizer.TokenizeLine("Loading…"));
    }
}

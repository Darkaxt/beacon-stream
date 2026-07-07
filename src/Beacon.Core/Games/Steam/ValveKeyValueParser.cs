using System.Text;

namespace Beacon.Core.Games.Steam;

internal sealed class ValveKeyValueObject
{
    private readonly Dictionary<string, ValveKeyValueNode> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ValveKeyValueNode> Values => _values;

    public bool TryGetObject(string key, out ValveKeyValueObject value)
    {
        if (_values.TryGetValue(key, out ValveKeyValueNode? node) && node.Object is not null)
        {
            value = node.Object;
            return true;
        }

        value = new ValveKeyValueObject();
        return false;
    }

    public bool TryGetString(string key, out string value)
    {
        if (_values.TryGetValue(key, out ValveKeyValueNode? node) && node.Value is not null)
        {
            value = node.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    internal void Add(string key, string value) => _values[key] = new ValveKeyValueNode(value, null);

    internal void Add(string key, ValveKeyValueObject value) => _values[key] = new ValveKeyValueNode(null, value);
}

internal sealed record ValveKeyValueNode(string? Value, ValveKeyValueObject? Object);

internal static class ValveKeyValueParser
{
    public static ValveKeyValueObject Parse(string text)
    {
        var parser = new Parser(text);
        return parser.ParseRoot();
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public ValveKeyValueObject ParseRoot()
        {
            var root = new ValveKeyValueObject();
            ParseInto(root, isRoot: true);
            return root;
        }

        private void ParseInto(ValveKeyValueObject target, bool isRoot)
        {
            while (true)
            {
                SkipWhitespace();
                if (_position >= text.Length)
                {
                    return;
                }

                if (text[_position] == '}')
                {
                    if (isRoot)
                    {
                        throw new FormatException("Unexpected closing brace in Steam VDF.");
                    }

                    _position++;
                    return;
                }

                string key = ReadQuotedString();
                SkipWhitespace();

                if (_position < text.Length && text[_position] == '{')
                {
                    _position++;
                    var child = new ValveKeyValueObject();
                    ParseInto(child, isRoot: false);
                    target.Add(key, child);
                    continue;
                }

                string value = ReadQuotedString();
                target.Add(key, value);
            }
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private string ReadQuotedString()
        {
            if (_position >= text.Length || text[_position] != '"')
            {
                throw new FormatException("Expected quoted string in Steam VDF.");
            }

            _position++;
            var builder = new StringBuilder();

            while (_position < text.Length)
            {
                char current = text[_position++];
                if (current == '"')
                {
                    return builder.ToString();
                }

                if (current == '\\' && _position < text.Length)
                {
                    char escaped = text[_position++];
                    if (escaped is '\\' or '"')
                    {
                        builder.Append(escaped);
                    }
                    else
                    {
                        builder.Append(current);
                        builder.Append(escaped);
                    }

                    continue;
                }

                builder.Append(current);
            }

            throw new FormatException("Unterminated quoted string in Steam VDF.");
        }
    }
}

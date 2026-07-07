using System.Text;

namespace Beacon.Core.Games.Steam;

public sealed record SteamShortcut(int AppId, string AppName, string Exe, string StartDir, string? Icon);

public static class SteamShortcutBinaryParser
{
    public static IReadOnlyList<SteamShortcut> Parse(byte[] bytes)
    {
        var parser = new Parser(bytes);
        return parser.Parse();
    }

    private sealed class Parser(byte[] bytes)
    {
        private readonly List<SteamShortcut> _shortcuts = [];
        private int _position;

        public IReadOnlyList<SteamShortcut> Parse()
        {
            while (!End)
            {
                byte type = ReadByte();
                if (type == 8)
                {
                    break;
                }

                string name = ReadString();
                if (type == 0 && name.Equals("shortcuts", StringComparison.OrdinalIgnoreCase))
                {
                    ParseShortcutsObject();
                }
                else
                {
                    SkipValue(type);
                }
            }

            return _shortcuts;
        }

        private bool End => _position >= bytes.Length;

        private void ParseShortcutsObject()
        {
            while (!End)
            {
                byte type = ReadByte();
                if (type == 8)
                {
                    return;
                }

                _ = ReadString();
                if (type == 0)
                {
                    SteamShortcut? shortcut = ParseShortcutObject();
                    if (shortcut is not null)
                    {
                        _shortcuts.Add(shortcut);
                    }
                }
                else
                {
                    SkipValue(type);
                }
            }
        }

        private SteamShortcut? ParseShortcutObject()
        {
            int? appId = null;
            string? appName = null;
            string? exe = null;
            string? startDir = null;
            string? icon = null;

            while (!End)
            {
                byte type = ReadByte();
                if (type == 8)
                {
                    break;
                }

                string name = ReadString();
                switch (type)
                {
                    case 0:
                        SkipObject();
                        break;
                    case 1:
                        string value = ReadString();
                        if (name.Equals("appname", StringComparison.OrdinalIgnoreCase))
                        {
                            appName = value;
                        }
                        else if (name.Equals("Exe", StringComparison.OrdinalIgnoreCase))
                        {
                            exe = value;
                        }
                        else if (name.Equals("StartDir", StringComparison.OrdinalIgnoreCase))
                        {
                            startDir = value;
                        }
                        else if (name.Equals("icon", StringComparison.OrdinalIgnoreCase))
                        {
                            icon = value;
                        }

                        break;
                    case 2:
                        int intValue = ReadInt32();
                        if (name.Equals("appid", StringComparison.OrdinalIgnoreCase))
                        {
                            appId = intValue;
                        }

                        break;
                    default:
                        SkipValue(type);
                        break;
                }
            }

            return appId.HasValue && appName is not null && exe is not null
                ? new SteamShortcut(appId.Value, appName, exe, startDir ?? string.Empty, icon)
                : null;
        }

        private void SkipObject()
        {
            while (!End)
            {
                byte type = ReadByte();
                if (type == 8)
                {
                    return;
                }

                _ = ReadString();
                SkipValue(type);
            }
        }

        private void SkipValue(byte type)
        {
            switch (type)
            {
                case 0:
                    SkipObject();
                    break;
                case 1:
                    _ = ReadString();
                    break;
                case 2:
                case 3:
                case 4:
                    SkipBytes(4);
                    break;
                case 7:
                    SkipBytes(8);
                    break;
                default:
                    throw new FormatException($"Unsupported Steam shortcut VDF value type {type}.");
            }
        }

        private byte ReadByte()
        {
            EnsureAvailable(1);
            return bytes[_position++];
        }

        private int ReadInt32()
        {
            EnsureAvailable(4);
            int value = BitConverter.ToInt32(bytes, _position);
            _position += 4;
            return value;
        }

        private string ReadString()
        {
            int start = _position;
            while (_position < bytes.Length && bytes[_position] != 0)
            {
                _position++;
            }

            EnsureAvailable(1);
            string value = Encoding.UTF8.GetString(bytes, start, _position - start);
            _position++;
            return value;
        }

        private void SkipBytes(int count)
        {
            EnsureAvailable(count);
            _position += count;
        }

        private void EnsureAvailable(int count)
        {
            if (_position + count > bytes.Length)
            {
                throw new FormatException("Unexpected end of Steam shortcut VDF.");
            }
        }
    }
}

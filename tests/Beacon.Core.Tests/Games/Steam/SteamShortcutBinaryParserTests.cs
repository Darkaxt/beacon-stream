using System.Text;
using Beacon.Core.Games.Steam;

namespace Beacon.Core.Tests.Games.Steam;

public sealed class SteamShortcutBinaryParserTests
{
    [Fact]
    public void ParsesStoredAppIdAndBuildsLongRungameId()
    {
        byte[] shortcut = SteamShortcutFixture.Create(
            appId: unchecked((int)0xFDFC9973),
            appName: "DRAGON QUEST III HD-2D Remake",
            exe: "\"D:\\Games\\Dragon Quest III HD-2D Remake\\DQIIIHD2DRemake.exe\"",
            startDir: "\"D:\\Games\\Dragon Quest III HD-2D Remake\"");

        SteamShortcut parsed = Assert.Single(SteamShortcutBinaryParser.Parse(shortcut));

        Assert.Equal(unchecked((int)0xFDFC9973), parsed.AppId);
        Assert.Equal("DRAGON QUEST III HD-2D Remake", parsed.AppName);
        Assert.Equal("\"D:\\Games\\Dragon Quest III HD-2D Remake\\DQIIIHD2DRemake.exe\"", parsed.Exe);
        Assert.Equal("steam://rungameid/18301671704960696320", SteamShortcutLaunchId.FromStoredAppId(parsed.AppId).ToUri());
    }

    private static class SteamShortcutFixture
    {
        public static byte[] Create(int appId, string appName, string exe, string startDir)
        {
            using var stream = new MemoryStream();
            WriteByte(stream, 0);
            WriteString(stream, "shortcuts");
            WriteByte(stream, 0);
            WriteString(stream, "0");
            WriteByte(stream, 2);
            WriteString(stream, "appid");
            stream.Write(BitConverter.GetBytes(appId));
            WriteByte(stream, 1);
            WriteString(stream, "appname");
            WriteString(stream, appName);
            WriteByte(stream, 1);
            WriteString(stream, "Exe");
            WriteString(stream, exe);
            WriteByte(stream, 1);
            WriteString(stream, "StartDir");
            WriteString(stream, startDir);
            WriteByte(stream, 1);
            WriteString(stream, "icon");
            WriteString(stream, "C:\\Icons\\dq3.ico");
            WriteByte(stream, 8);
            WriteByte(stream, 8);
            return stream.ToArray();
        }

        private static void WriteByte(Stream stream, byte value) => stream.WriteByte(value);

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes);
            stream.WriteByte(0);
        }
    }
}

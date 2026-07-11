using System.Collections;
using System.Globalization;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Beacon.StreamWorker.Contracts.Diagnostics;

public static class ContractDiagnosticFormatter
{
    private static readonly string[] SensitiveFieldFragments =
    {
        "ticket",
        "credential",
        "private_key",
        "input_payload",
    };

    public static string Format(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var output = new StringBuilder();
        AppendMessage(output, message);
        return output.ToString();
    }

    private static void AppendMessage(StringBuilder output, IMessage message)
    {
        output.Append(message.Descriptor.Name).Append('{');
        var first = true;
        foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
        {
            var value = field.Accessor.GetValue(message);
            if (value is null || IsDefaultScalar(value))
            {
                continue;
            }

            if (!first)
            {
                output.Append(", ");
            }

            first = false;
            output.Append(field.Name).Append('=');
            if (IsSensitive(field))
            {
                output.Append("<redacted:").Append(GetValueLength(value)).Append("-bytes>");
            }
            else
            {
                AppendValue(output, value);
            }
        }

        output.Append('}');
    }

    private static void AppendValue(StringBuilder output, object value)
    {
        switch (value)
        {
            case IMessage nested:
                AppendMessage(output, nested);
                break;
            case ByteString bytes:
                output.Append("<bytes:").Append(bytes.Length).Append('>');
                break;
            case string text:
                output.Append('"').Append(text).Append('"');
                break;
            case IEnumerable values:
                output.Append('[');
                var first = true;
                foreach (var item in values)
                {
                    if (!first)
                    {
                        output.Append(", ");
                    }

                    first = false;
                    AppendValue(output, item!);
                }

                output.Append(']');
                break;
            default:
                output.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static bool IsSensitive(FieldDescriptor field) =>
        SensitiveFieldFragments.Any(
            fragment => field.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static int GetValueLength(object value) => value switch
    {
        ByteString bytes => bytes.Length,
        string text => Encoding.UTF8.GetByteCount(text),
        _ => 0,
    };

    private static bool IsDefaultScalar(object value) => value switch
    {
        bool boolean => !boolean,
        int number => number == 0,
        uint number => number == 0,
        long number => number == 0,
        ulong number => number == 0,
        float number => number == 0,
        double number => number == 0,
        string text => text.Length == 0,
        ByteString bytes => bytes.Length == 0,
        Enum enumeration => Convert.ToInt32(enumeration, CultureInfo.InvariantCulture) == 0,
        _ => false,
    };
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoGameMmorpgEditor.Ui;

public sealed class PixelTextRenderer
{
    private readonly Texture2D _pixel;

    public PixelTextRenderer(GraphicsDevice graphicsDevice)
    {
        _pixel = new Texture2D(graphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void DrawText(SpriteBatch spriteBatch, string text, int x, int y, Color color, int scale = 2)
        => DrawText(spriteBatch, text, x, y, color, scale, maxWidth: null);

    public void DrawText(SpriteBatch spriteBatch, string text, int x, int y, Color color, int scale, int? maxWidth)
    {
        var cursorX = x;
        var maxX = maxWidth.HasValue ? x + Math.Max(0, maxWidth.Value) : int.MaxValue;
        foreach (var rawCharacter in text)
        {
            var character = char.ToUpperInvariant(rawCharacter);
            if (character == '\n')
            {
                cursorX = x;
                y += 8 * scale;
                continue;
            }

            if (character == ' ')
            {
                cursorX += 4 * scale;
                continue;
            }

            if (cursorX + (5 * scale) > maxX)
            {
                break;
            }

            DrawCharacter(spriteBatch, character, cursorX, y, color, scale);
            cursorX += 6 * scale;
        }
    }

    public int DrawWrappedText(
        SpriteBatch spriteBatch,
        string text,
        int x,
        int y,
        int maxWidth,
        int maxHeight,
        Color color,
        int scale = 1,
        int lineSpacing = 2)
    {
        var lineHeight = (7 * scale) + lineSpacing;
        var currentY = y;
        foreach (var line in WrapLines(text, maxWidth, scale))
        {
            if (currentY + (7 * scale) > y + maxHeight)
            {
                break;
            }

            DrawText(spriteBatch, line, x, currentY, color, scale, maxWidth);
            currentY += lineHeight;
        }

        return currentY;
    }

    private static IEnumerable<string> WrapLines(string text, int maxWidth, int scale)
    {
        var maxChars = Math.Max(1, maxWidth / Math.Max(1, 6 * scale));
        foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var line = string.Empty;
            foreach (var word in words)
            {
                if (line.Length == 0)
                {
                    line = word;
                    continue;
                }

                if (line.Length + 1 + word.Length <= maxChars)
                {
                    line += " " + word;
                    continue;
                }

                yield return line;
                line = word;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }

    private void DrawCharacter(SpriteBatch spriteBatch, char character, int x, int y, Color color, int scale)
    {
        var pattern = GetPattern(character);
        for (var row = 0; row < pattern.Length; row++)
        {
            var line = pattern[row];
            for (var column = 0; column < line.Length; column++)
            {
                if (line[column] != '1')
                {
                    continue;
                }

                spriteBatch.Draw(_pixel, new Rectangle(x + (column * scale), y + (row * scale), scale, scale), color);
            }
        }
    }

    private static string[] GetPattern(char character)
    {
        return character switch
        {
            'A' => new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            'B' => new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            'C' => new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" },
            'D' => new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
            'E' => new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
            'F' => new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
            'G' => new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" },
            'H' => new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
            'I' => new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" },
            'J' => new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" },
            'K' => new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" },
            'L' => new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            'M' => new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" },
            'N' => new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" },
            'O' => new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
            'P' => new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
            'Q' => new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" },
            'R' => new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            'S' => new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
            'T' => new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
            'U' => new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
            'V' => new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
            'W' => new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" },
            'X' => new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
            'Y' => new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
            'Z' => new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
            '0' => new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
            '1' => new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            '2' => new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            '3' => new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
            '4' => new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            '5' => new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" },
            '6' => new[] { "01111", "10000", "10000", "11110", "10001", "10001", "01110" },
            '7' => new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            '8' => new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
            '9' => new[] { "01110", "10001", "10001", "01111", "00001", "00001", "11110" },
            ':' => new[] { "00000", "00100", "00100", "00000", "00100", "00100", "00000" },
            '.' => new[] { "00000", "00000", "00000", "00000", "00000", "01100", "01100" },
            ',' => new[] { "00000", "00000", "00000", "00000", "00100", "00100", "01000" },
            '-' => new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
            '_' => new[] { "00000", "00000", "00000", "00000", "00000", "00000", "11111" },
            '/' => new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" },
            '=' => new[] { "00000", "11111", "00000", "11111", "00000", "00000", "00000" },
            '+' => new[] { "00000", "00100", "00100", "11111", "00100", "00100", "00000" },
            '?' => new[] { "01110", "10001", "00001", "00010", "00100", "00000", "00100" },
            _ => new[] { "11111", "10001", "00010", "00100", "00000", "00100", "00000" }
        };
    }
}

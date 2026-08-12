using System.Text;

namespace Api.Services;

/// <summary>
/// Just enough RFC 4180 to read a spreadsheet export: comma-separated fields, optionally
/// double-quoted, with "" for a literal quote inside one. Quoted fields may span lines.
///
/// Hand-written rather than pulled from a package because this is the only CSV the app
/// will ever read, and the format it has to survive is the one Excel and Google Sheets
/// produce.
/// </summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        // Spreadsheet exports routinely start with a UTF-8 BOM, which would otherwise end
        // up glued to the first column's name.
        var i = text[0] == '﻿' ? 1 : 0;

        while (i < text.Length)
        {
            var c = text[i++];

            if (inQuotes)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i < text.Length && text[i] == '"')
                {
                    // A doubled quote is one literal quote, so step over the second.
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break; // the \n that follows ends the row
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0) EndRow();

        return rows;

        void EndRow()
        {
            fields.Add(field.ToString());
            field.Clear();

            // Skip blank lines, including the trailing newline every file ends with.
            if (fields.Exists(f => f.Length > 0)) rows.Add([.. fields]);

            fields.Clear();
        }
    }
}

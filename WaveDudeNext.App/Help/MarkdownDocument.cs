// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using NLog;

namespace WaveDudeNext.App.Help;

/// <summary>A link of a rendered document was activated: "#anchor" for a heading, else a web or mail address.</summary>
/// <param name="target">The link's destination as written.</param>
/// <param name="title">The link's title, or null.</param>
public sealed class MarkdownLinkEventArgs(string target, string? title) : EventArgs
{
	/// <summary>The link's destination as written: "#anchor", or an http, https or mailto address.</summary>
	public string Target { get; } = target;

	/// <summary>The link's title, or null.</summary>
	public string? Title { get; } = title;
}

/// <summary>How a <see cref="MarkdownDocument"/> is rendered.</summary>
public sealed class MarkdownRenderOptions
{
	/// <summary>
	/// Whether a tight list (no blank line between its items) shows its items without paragraph spacing, as GitHub does;
	/// without it every item's paragraph keeps its spacing, the licence's look.
	/// </summary>
	public bool TightLists { get; init; } = true;

	/// <summary>
	/// What a figure's image source ("Assets/main-window.svg") becomes: the control, or null when there is nothing to
	/// show (the alt text shows then, in italics, with a warning). The default reads the SVGs embedded under
	/// <c>Assets/</c> through <see cref="SvgFigure.FromResource"/>.
	/// </summary>
	public Func<string, Control?>? FigureResolver { get; init; }

	/// <summary>The document's name in log lines.</summary>
	public string Name { get; init; } = "document";
}

/// <summary>
/// A Markdown document as Avalonia controls, rendered at run time: the port's replacement for upstream's build-time
/// conversion of MANUAL.md and LICENSE.md into WPF FlowDocuments (<c>Build/MarkdownToFlowDocument.cs</c>), whose parser
/// this is a port of. The output is a <see cref="StackPanel"/> of blocks: headings and paragraphs as selectable text
/// blocks with inlines, lists as rows of a marker and the item's blocks, quotes behind a 3 px bar, code in a monospace
/// font, rules as 1 px lines, and figures (an image alone in its paragraph) as the controls the figure resolver gives,
/// in a <see cref="Viewbox"/> that only shrinks them. Each heading gets GitHub's anchor (a repeated one numbered -1,
/// -2); a link to "#anchor" or to the web is a button styled as a link (Tab reaches it) that raises
/// <see cref="LinkActivated"/> for the window to act on. UI thread (it makes controls); one instance per rendering.
/// </summary>
/// <remarks>
/// The CommonMark it knows, as upstream's: ATX and setext headings; paragraphs, a line break inside one a space, two
/// trailing spaces or a backslash a hard break; bullet and ordered lists nested by indentation, with lazy continuation
/// lines, tight or loose; block quotes; indented code (the block's common indentation removed) and fenced code;
/// thematic breaks; HTML comments, dropped. Inline: emphasis and strong emphasis by CommonMark's delimiter rules, code
/// spans, inline links, angle-bracket autolinks and GitHub's bare URLs, backslash escapes and entities. Other Markdown
/// (tables, raw HTML, reference links) shows as its text.
/// </remarks>
public sealed partial class MarkdownDocument
{
	/// <summary>The font of code spans and code blocks: Consolas where it exists (Windows), else a common monospace, else the platform's.</summary>
	public static readonly FontFamily CodeFont = new("Consolas, Cascadia Mono, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace");

	private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

	[GeneratedRegex(@"^(#{1,6})(?:[ \t]+(.*))?$")]
	private static partial Regex Atx();

	[GeneratedRegex(@"(?:^|[ \t]+)#+[ \t]*$")]
	private static partial Regex AtxClosing();

	[GeneratedRegex(@"^(?:=+|-+)[ \t]*$")]
	private static partial Regex Setext();

	[GeneratedRegex(@"^(?:(?:\*[ \t]*){3,}|(?:-[ \t]*){3,}|(?:_[ \t]*){3,})$")]
	private static partial Regex Rule();

	[GeneratedRegex(@"^(`{3,}|~{3,})(.*)$")]
	private static partial Regex Fence();

	[GeneratedRegex(@"\G<([A-Za-z][A-Za-z0-9+.\-]{1,31}:[^\s<>]*)>")]
	private static partial Regex AutoLink();

	[GeneratedRegex(@"\G<([A-Za-z0-9.!#$%&'*+/=?^_`{|}~\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)*)>")]
	private static partial Regex EmailLink();

	[GeneratedRegex(@"\G(?:https?://|www\.)[^\s<]+", RegexOptions.IgnoreCase)]
	private static partial Regex BareUrl();

	[GeneratedRegex(@"\G&(?:#[xX]([0-9A-Fa-f]{1,6})|#([0-9]{1,7})|([A-Za-z][A-Za-z0-9]{1,31}));")]
	private static partial Regex Entity();

	[GeneratedRegex(@"&[A-Za-z0-9]+;$")]
	private static partial Regex TrailingEntity();

	[GeneratedRegex(@"^[A-Za-z0-9_.\-:]+$")]
	private static partial Regex Host();

	private static readonly Dictionary<string, string> Entities = new()
	{
		{ "amp", "&" }, { "lt", "<" }, { "gt", ">" }, { "quot", "\"" }, { "apos", "'" }, { "nbsp", " " },
		{ "copy", "©" }, { "reg", "®" }, { "trade", "™" }, { "mdash", "—" }, { "ndash", "–" },
		{ "hellip", "…" }, { "times", "×" }, { "deg", "°" }, { "plusmn", "±" }, { "middot", "·" },
		{ "larr", "←" }, { "rarr", "→" }, { "uarr", "↑" }, { "darr", "↓" }, { "harr", "↔" },
		{ "lsquo", "‘" }, { "rsquo", "’" }, { "ldquo", "“" }, { "rdquo", "”" }, { "laquo", "«" },
		{ "raquo", "»" }, { "bull", "•" }, { "micro", "µ" }, { "lambda", "λ" }, { "radic", "√" },
	};

	private readonly MarkdownRenderOptions _options;
	private readonly HashSet<string> _anchors = new(StringComparer.Ordinal);
	private readonly List<ManualHeading> _headings = [];
	private int _figures;

	private MarkdownDocument(MarkdownRenderOptions options)
	{
		_options = options;
		Root = new StackPanel { Margin = new Thickness(10) };
	}

	/// <summary>The rendered document: the blocks stacked, with the page padding of 10 px around them.</summary>
	public StackPanel Root { get; }

	/// <summary>Every heading in document order, the title included: what a link to "#anchor" is looked up in.</summary>
	public IReadOnlyList<ManualHeading> Headings => _headings;

	/// <summary>How many figures the document shows.</summary>
	public int FigureCount => _figures;

	/// <summary>A link in the text was activated: the window scrolls to the heading or opens the address.</summary>
	public event EventHandler<MarkdownLinkEventArgs>? LinkActivated;

	/// <summary>Renders <paramref name="markdown"/>; never throws for the text's sake, odd Markdown shows as text and a missing figure as its alt text.</summary>
	public static MarkdownDocument Render(string markdown, MarkdownRenderOptions? options = null)
	{
		var document = new MarkdownDocument(options ?? new MarkdownRenderOptions());
		document.Build(markdown);
		return document;
	}

	/// <summary>The text of a resource embedded in this assembly under <paramref name="logicalName"/> ("MANUAL.md", "LICENSE.md"), or null when the build lacks it.</summary>
	public static string? ReadEmbedded(string logicalName)
	{
		using Stream? stream = typeof(MarkdownDocument).Assembly.GetManifestResourceStream(logicalName);
		if (stream == null)
		{
			return null;
		}
		using var reader = new StreamReader(stream, Encoding.UTF8);
		return reader.ReadToEnd();
	}

	/// <summary>The document's headings as a tree, each under the nearest heading of a lower level before it.</summary>
	public IReadOnlyList<ManualHeading> Contents()
	{
		var roots = new List<ManualHeading>();
		var open = new Stack<ManualHeading>();
		foreach (ManualHeading heading in _headings)
		{
			while (open.Count > 0 && open.Peek().Level >= heading.Level)
			{
				open.Pop();
			}
			(open.Count > 0 ? open.Peek().Children : roots).Add(heading);
			open.Push(heading);
		}
		return roots;
	}

	/// <summary>The heading a link "#anchor" names, or null.</summary>
	public ManualHeading? Find(string anchor) => _headings.FirstOrDefault(h => h.Anchor == anchor);

	private void Build(string markdown)
	{
		string[] text = markdown.Split('\n');
		var lines = new List<SourceLine>(text.Length);
		for (int i = 0; i < text.Length; i++)
		{
			lines.Add(new SourceLine(ExpandTabs(text[i].TrimEnd('\r')), i + 1));
		}
		List<Block> blocks = ParseBlocks(lines);
		Prepare(blocks);
		RenderBlocks(blocks, Root, 0, false);
	}

	// ------------------------------------------------------------------ blocks

	private enum BlockKind { Paragraph, Heading, Code, Rule, List, Quote }

	private readonly record struct SourceLine(string Text, int Number);

	private sealed class Block
	{
		public BlockKind Kind;
		/// <summary>The 1-based line of the Markdown the block starts on, for messages.</summary>
		public int Line;
		/// <summary>The last line the block takes; a blank line between two blocks of a list item makes the list loose.</summary>
		public int EndLine;
		public int Level;
		/// <summary>A paragraph's or heading's text lines, or a code block's lines.</summary>
		public readonly List<string> Lines = [];
		public bool Ordered;
		/// <summary>A list's bullet character, or an ordered list's delimiter ('.' or ')').</summary>
		public char Marker;
		public int Start = 1;
		public bool Tight = true;
		public readonly List<List<Block>> Items = [];
		public List<Block> Children = [];
		public Node Inlines = new(NodeKind.Root);
		public string Anchor = "";
	}

	private struct ListMarker
	{
		public bool Ordered;
		public char Char;
		public int Number;
		/// <summary>The column the item's content starts at; its other lines belong to it from this indentation on.</summary>
		public int ContentIndent;
		public string Content;
		public bool Empty;
	}

	private List<Block> ParseBlocks(List<SourceLine> lines)
	{
		var blocks = new List<Block>();
		int i = 0;
		while (i < lines.Count)
		{
			string line = lines[i].Text;
			if (IsBlank(line))
			{
				i++;
				continue;
			}
			int indent = Indent(line);
			if (indent >= 4)
			{
				i = ParseIndentedCode(lines, i, blocks);
				continue;
			}
			string rest = line[indent..];
			Match match;
			if (rest.StartsWith("<!--", StringComparison.Ordinal))
			{
				i = SkipComment(lines, i);
			}
			else if (IsFence(rest, out match))
			{
				i = ParseFence(lines, i, indent, match.Groups[1].Value, blocks);
			}
			else if ((match = Atx().Match(rest)).Success)
			{
				var heading = new Block { Kind = BlockKind.Heading, Level = match.Groups[1].Length, Line = lines[i].Number, EndLine = lines[i].Number };
				heading.Lines.Add(AtxClosing().Replace(match.Groups[2].Value.Trim(), "").Trim());
				blocks.Add(heading);
				i++;
			}
			else if (Rule().IsMatch(rest))
			{
				blocks.Add(new Block { Kind = BlockKind.Rule, Line = lines[i].Number, EndLine = lines[i].Number });
				i++;
			}
			else if (rest[0] == '>')
			{
				i = ParseQuote(lines, i, blocks);
			}
			else if (TryListMarker(line, out ListMarker marker))
			{
				i = ParseList(lines, i, marker, blocks);
			}
			else
			{
				i = ParseParagraph(lines, i, blocks);
			}
		}
		return blocks;
	}

	private static bool IsFence(string rest, out Match match)
	{
		match = Fence().Match(rest);
		return match.Success && !(match.Groups[1].Value[0] == '`' && match.Groups[2].Value.Contains('`'));
	}

	// the lines indented by four or more, blank ones between them included; the common indentation is removed
	private static int ParseIndentedCode(List<SourceLine> lines, int i, List<Block> blocks)
	{
		var block = new Block { Kind = BlockKind.Code, Line = lines[i].Number };
		var code = new List<string>();
		int j = i;
		while (j < lines.Count && (Indent(lines[j].Text) >= 4 || IsBlank(lines[j].Text)))
		{
			if (!IsBlank(lines[j].Text))
			{
				block.EndLine = lines[j].Number;
			}
			code.Add(lines[j].Text);
			j++;
		}
		while (code.Count > 0 && IsBlank(code[^1]))
		{
			code.RemoveAt(code.Count - 1);
		}
		int indent = int.MaxValue;
		foreach (string c in code)
		{
			if (!IsBlank(c))
			{
				indent = Math.Min(indent, Indent(c));
			}
		}
		foreach (string c in code)
		{
			block.Lines.Add(c.Length >= indent ? c[indent..] : "");
		}
		blocks.Add(block);
		return j;
	}

	// an HTML comment block, dropped up to the line that closes it
	private static int SkipComment(List<SourceLine> lines, int i)
	{
		int start = lines[i].Text.IndexOf("<!--", StringComparison.Ordinal) + 4;
		for (int j = i; j < lines.Count; j++)
		{
			if (lines[j].Text.IndexOf("-->", j == i ? start : 0, StringComparison.Ordinal) >= 0)
			{
				return j + 1;
			}
		}
		return lines.Count;
	}

	private static int ParseFence(List<SourceLine> lines, int i, int indent, string fence, List<Block> blocks)
	{
		var block = new Block { Kind = BlockKind.Code, Line = lines[i].Number };
		int j = i + 1;
		while (j < lines.Count)
		{
			string line = lines[j].Text;
			int lineIndent = Indent(line);
			string rest = line[lineIndent..].TrimEnd();
			j++;
			if (lineIndent < 4 && rest.Length >= fence.Length && rest.Trim(fence[0]).Length == 0)
			{
				break;
			}
			block.Lines.Add(line[Math.Min(indent, lineIndent)..]);
		}
		block.EndLine = lines[j - 1].Number;
		blocks.Add(block);
		return j;
	}

	private int ParseQuote(List<SourceLine> lines, int i, List<Block> blocks)
	{
		int start = i;
		var inner = new List<SourceLine>();
		while (i < lines.Count)
		{
			string line = lines[i].Text;
			int indent = Indent(line);
			if (indent < 4 && indent < line.Length && line[indent] == '>')
			{
				string rest = line[(indent + 1)..];
				inner.Add(new SourceLine(rest.StartsWith(' ') ? rest[1..] : rest, lines[i].Number));
			}
			else if (!IsBlank(line) && inner.Count > 0 && Continues(inner) && !InterruptsParagraph(line))
			{
				inner.Add(new SourceLine(line.TrimStart(' '), lines[i].Number));
			}
			else
			{
				break;
			}
			i++;
		}
		blocks.Add(new Block { Kind = BlockKind.Quote, Line = lines[start].Number, EndLine = lines[i - 1].Number, Children = ParseBlocks(inner) });
		return i;
	}

	private int ParseList(List<SourceLine> lines, int i, ListMarker first, List<Block> blocks)
	{
		var list = new Block { Kind = BlockKind.List, Line = lines[i].Number, Ordered = first.Ordered, Marker = first.Char, Start = first.Number };
		while (i < lines.Count && TryListMarker(lines[i].Text, out ListMarker marker) && marker.Ordered == list.Ordered && marker.Char == list.Marker)
		{
			var item = new List<SourceLine> { new(marker.Content, lines[i].Number) };
			list.EndLine = lines[i].Number;
			i++;
			while (i < lines.Count)
			{
				string line = lines[i].Text;
				if (IsBlank(line))
				{
					int k = i;
					while (k < lines.Count && IsBlank(lines[k].Text))
					{
						k++;
					}
					// blank lines belong to the item when an indented line follows; an item that began empty ends at the first
					if (k < lines.Count && Indent(lines[k].Text) >= marker.ContentIndent && !(marker.Empty && item.Count == 1))
					{
						for (; i < k; i++)
						{
							item.Add(new SourceLine("", lines[i].Number));
						}
						continue;
					}
					break;
				}
				if (Indent(line) >= marker.ContentIndent)
				{
					item.Add(new SourceLine(line[marker.ContentIndent..], lines[i].Number));
					list.EndLine = lines[i].Number;
					i++;
					continue;
				}
				if (TryListMarker(line, out _) || !Continues(item) || InterruptsParagraph(line))
				{
					break;
				}
				// a lazy continuation line of the item's paragraph
				item.Add(new SourceLine(line.TrimStart(' '), lines[i].Number));
				list.EndLine = lines[i].Number;
				i++;
			}
			List<Block> content = ParseBlocks(item);
			// a blank line between two of the item's own blocks makes the list loose (one inside a nested list does not)
			for (int b = 1; b < content.Count; b++)
			{
				if (content[b].Line > content[b - 1].EndLine + 1)
				{
					list.Tight = false;
				}
			}
			list.Items.Add(content);
			int after = i;
			while (after < lines.Count && IsBlank(lines[after].Text))
			{
				after++;
			}
			if (after > i)
			{
				if (after < lines.Count && TryListMarker(lines[after].Text, out ListMarker next) && next.Ordered == list.Ordered && next.Char == list.Marker)
				{
					list.Tight = false;
					i = after;
				}
				else
				{
					break;
				}
			}
		}
		blocks.Add(list);
		return i;
	}

	private static int ParseParagraph(List<SourceLine> lines, int i, List<Block> blocks)
	{
		var block = new Block { Kind = BlockKind.Paragraph, Line = lines[i].Number };
		block.Lines.Add(lines[i].Text.TrimStart(' '));
		int j = i + 1;
		for (; j < lines.Count; j++)
		{
			string line = lines[j].Text;
			if (IsBlank(line))
			{
				break;
			}
			int indent = Indent(line);
			if (indent < 4 && Setext().IsMatch(line[indent..]))
			{
				block.Kind = BlockKind.Heading;
				block.Level = line[indent] == '=' ? 1 : 2;
				j++;
				break;
			}
			if (InterruptsParagraph(line))
			{
				break;
			}
			block.Lines.Add(line.TrimStart(' '));
		}
		block.EndLine = lines[j - 1].Number;
		blocks.Add(block);
		return j;
	}

	// whether a line starts a block that ends a paragraph before it: a heading, a fence, a rule, a comment, a quote, or
	// a list item with content (an ordered one only from 1)
	private static bool InterruptsParagraph(string line)
	{
		int indent = Indent(line);
		if (indent >= 4 || IsBlank(line))
		{
			return false;
		}
		string rest = line[indent..];
		return rest.StartsWith("<!--", StringComparison.Ordinal) || rest[0] == '>' || Atx().IsMatch(rest) || Rule().IsMatch(rest) || IsFence(rest, out _)
			|| (TryListMarker(line, out ListMarker marker) && !marker.Empty && (!marker.Ordered || marker.Number == 1));
	}

	// whether the last line taken so far is paragraph text a lazy line may continue
	private static bool Continues(List<SourceLine> taken)
	{
		string last = taken[^1].Text;
		if (IsBlank(last) || Indent(last) >= 4)
		{
			return false;
		}
		string rest = last.TrimStart(' ');
		return !Atx().IsMatch(rest) && !Rule().IsMatch(rest) && !IsFence(rest, out _);
	}

	private static bool TryListMarker(string line, out ListMarker marker)
	{
		marker = new ListMarker { Content = "" };
		int indent = Indent(line);
		if (indent > 3)
		{
			return false;
		}
		string rest = line[indent..];
		if (rest.Length == 0 || Rule().IsMatch(rest))
		{
			return false;
		}
		int width;
		if (rest[0] is '-' or '+' or '*')
		{
			marker.Char = rest[0];
			width = 1;
		}
		else
		{
			int digits = 0;
			while (digits < rest.Length && digits < 10 && rest[digits] >= '0' && rest[digits] <= '9')
			{
				digits++;
			}
			if (digits == 0 || digits > 9 || digits >= rest.Length || (rest[digits] != '.' && rest[digits] != ')'))
			{
				return false;
			}
			marker.Ordered = true;
			marker.Char = rest[digits];
			marker.Number = int.Parse(rest[..digits], CultureInfo.InvariantCulture);
			width = digits + 1;
		}
		if (width < rest.Length && rest[width] != ' ')
		{
			return false;
		}
		int spaces = 0;
		while (width + spaces < rest.Length && rest[width + spaces] == ' ')
		{
			spaces++;
		}
		if (width + spaces == rest.Length)
		{
			marker.Empty = true;
			marker.Content = "";
			marker.ContentIndent = indent + width + 1;
		}
		else if (spaces > 4)
		{
			// five spaces or more: the item starts with indented code, its content one space after the marker
			marker.Content = rest[(width + 1)..];
			marker.ContentIndent = indent + width + 1;
		}
		else
		{
			marker.Content = rest[(width + spaces)..];
			marker.ContentIndent = indent + width + spaces;
		}
		return true;
	}

	// the inline content of every paragraph and heading, and the headings' anchors
	private void Prepare(List<Block> blocks)
	{
		foreach (Block block in blocks)
		{
			switch (block.Kind)
			{
				case BlockKind.Paragraph:
					block.Inlines = new InlineParser(string.Join("\n", block.Lines).TrimEnd()).Parse();
					break;
				case BlockKind.Heading:
					block.Inlines = new InlineParser(string.Join("\n", block.Lines).Trim()).Parse();
					block.Anchor = UniqueAnchor(Slug(PlainText(block.Inlines)));
					break;
				case BlockKind.List:
					foreach (List<Block> item in block.Items)
					{
						Prepare(item);
					}
					break;
				case BlockKind.Quote:
					Prepare(block.Children);
					break;
			}
		}
	}

	/// <summary>GitHub's anchor of a heading text: lower case, letters, digits, marks, hyphens and underscores kept, a hyphen for each space.</summary>
	public static string Slug(string text)
	{
		var sb = new StringBuilder();
		foreach (char c in text.Trim().ToLowerInvariant())
		{
			UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
			if (c == ' ')
			{
				sb.Append('-');
			}
			else if (c == '-' || char.IsLetterOrDigit(c) || category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark
				or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	private string UniqueAnchor(string slug)
	{
		string anchor = slug;
		for (int n = 1; _anchors.Contains(anchor); n++)
		{
			anchor = slug + "-" + n.ToString(CultureInfo.InvariantCulture);
		}
		_anchors.Add(anchor);
		return anchor;
	}

	// ------------------------------------------------------------------ rendering

	private void RenderBlocks(List<Block> blocks, Panel into, int depth, bool tight)
	{
		foreach (Block block in blocks)
		{
			switch (block.Kind)
			{
				case BlockKind.Heading:
					var heading = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
					(heading.FontSize, heading.Margin) = block.Level switch
					{
						1 => (18, new Thickness(0, 6, 0, 10)),
						2 => (15, new Thickness(0, 14, 0, 8)),
						_ => (13, new Thickness(0, 10, 0, 6)),
					};
					RenderInlines(block.Inlines, heading.Inlines!, block.Line, false);
					heading.Tag = "h" + block.Level.ToString(CultureInfo.InvariantCulture) + "#" + block.Anchor;
					_headings.Add(new ManualHeading(PlainText(block.Inlines), block.Level, block.Anchor, heading));
					into.Children.Add(heading);
					break;
				case BlockKind.Paragraph:
					Node? image = Figure(block.Inlines);
					if (image != null)
					{
						RenderFigure(image, block.Line, into);
					}
					else
					{
						into.Children.Add(Paragraph(block, tight));
					}
					break;
				case BlockKind.Code:
					into.Children.Add(new SelectableTextBlock
					{
						Text = string.Join("\n", block.Lines),
						FontFamily = CodeFont,
						FontSize = 11,
						Margin = new Thickness(16, 0, 0, 10),
						TextWrapping = TextWrapping.Wrap,
					});
					break;
				case BlockKind.Rule:
					into.Children.Add(new Border { Classes = { "rule" } });
					break;
				case BlockKind.List:
					RenderList(block, into, depth);
					break;
				case BlockKind.Quote:
					var quote = new StackPanel();
					RenderBlocks(block.Children, quote, depth, false);
					into.Children.Add(new Border { Classes = { "quote" }, Child = quote });
					break;
			}
		}
	}

	private SelectableTextBlock Paragraph(Block block, bool tight)
	{
		var paragraph = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Margin = tight ? new Thickness(0) : new Thickness(0, 0, 0, 10) };
		RenderInlines(block.Inlines, paragraph.Inlines!, block.Line, false);
		return paragraph;
	}

	// a list: a row per item, the marker in a gutter of 20 px (bullets) or 28 px (numbers), the item's blocks beside it
	private void RenderList(Block list, Panel into, int depth)
	{
		var panel = new StackPanel { Margin = depth == 0 ? new Thickness(0, 0, 0, 10) : new Thickness(0, 4, 0, 0) };
		bool tight = list.Tight && _options.TightLists;
		string bullet = depth == 0 ? "•" : depth == 1 ? "◦" : "▪";
		int number = list.Start;
		foreach (List<Block> item in list.Items)
		{
			var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 4) };
			var marker = new TextBlock
			{
				Text = list.Ordered ? number.ToString(CultureInfo.InvariantCulture) + "." : bullet,
				Width = list.Ordered ? 28 : 20,
				Padding = new Thickness(0, 0, 6, 0),
				TextAlignment = TextAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
			};
			row.Children.Add(marker);
			var content = new StackPanel();
			Grid.SetColumn(content, 1);
			if (item.Count == 1 && item[0].Kind == BlockKind.Paragraph && Figure(item[0].Inlines) == null)
			{
				content.Children.Add(Paragraph(item[0], tight));
			}
			else
			{
				RenderBlocks(item, content, depth + 1, tight);
			}
			row.Children.Add(content);
			panel.Children.Add(row);
			number++;
		}
		into.Children.Add(panel);
	}

	private void RenderInlines(Node parent, InlineCollection into, int line, bool inLink)
	{
		for (Node? n = parent.First; n != null; n = n.Next)
		{
			switch (n.Kind)
			{
				case NodeKind.Text:
					into.Add(new Run(n.Text));
					break;
				case NodeKind.SoftBreak:
					into.Add(new Run(" "));
					break;
				case NodeKind.HardBreak:
					into.Add(new LineBreak());
					break;
				case NodeKind.Code:
					into.Add(new Run(n.Text) { FontFamily = CodeFont });
					break;
				case NodeKind.Emphasis:
					var italic = new Italic();
					RenderInlines(n, italic.Inlines, line, inLink);
					into.Add(italic);
					break;
				case NodeKind.Strong:
					var strong = new Span { FontWeight = FontWeight.SemiBold };
					RenderInlines(n, strong.Inlines, line, inLink);
					into.Add(strong);
					break;
				case NodeKind.Link:
					RenderLink(n, into, line, inLink);
					break;
				case NodeKind.Image:
					Logger.Warn("{Name}({Line}): the image {Image} shares its paragraph with text; only an image alone in its paragraph is a figure, here its description shows", _options.Name, line, n.Text);
					var alt = new Italic();
					RenderInlines(n, alt.Inlines, line, true);
					into.Add(alt);
					break;
			}
		}
	}

	// a link to a heading ("#anchor") or to the web becomes a button styled as a link; one to a file shows as its text,
	// the app having nothing to open there
	private void RenderLink(Node link, InlineCollection into, int line, bool inLink)
	{
		string target = link.Text ?? "";
		bool anchor = target.StartsWith('#');
		bool web = Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "mailto";
		if (inLink || !(anchor || web))
		{
			if (!inLink)
			{
				Logger.Debug("{Name}({Line}): the link to {Target} shows as its text, the app has nothing to open there", _options.Name, line, target);
			}
			RenderInlines(link, into, line, true);
			return;
		}
		if (anchor && !_anchors.Contains(target[1..]))
		{
			Logger.Warn("{Name}({Line}): the link {Target} names no heading of the document", _options.Name, line, target);
		}
		var text = new TextBlock { TextWrapping = TextWrapping.NoWrap };
		RenderInlines(link, text.Inlines!, line, true);
		var button = new Button { Classes = { "link" }, Content = text, Tag = target };
		if (link.Title != null)
		{
			ToolTip.SetTip(button, link.Title);
		}
		AutomationProperties.SetName(button, PlainText(link));
		button.Click += (_, _) => LinkActivated?.Invoke(this, new MarkdownLinkEventArgs(target, link.Title));
		// the button's bottom on the line's bottom: its text shares the line's font, so the baselines meet
		into.Add(new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Bottom });
	}

	// the image when it stands alone in its paragraph, else null
	private static Node? Figure(Node inlines)
	{
		Node? image = null;
		for (Node? n = inlines.First; n != null; n = n.Next)
		{
			if (n.Kind == NodeKind.Image && image == null)
			{
				image = n;
			}
			else if (!(n.Kind == NodeKind.Text && IsBlank(n.Text)))
			{
				return null;
			}
		}
		return image;
	}

	// a figure: the picture scaled down to the column when it is wider, never up
	private void RenderFigure(Node image, int line, Panel into)
	{
		string source = image.Text ?? "";
		Control? body = ResolveFigure(source);
		if (body == null)
		{
			Logger.Warn("{Name}({Line}): the image {Image} cannot be shown; its description shows", _options.Name, line, source);
			var alt = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
			var italic = new Italic();
			RenderInlines(image, italic.Inlines, line, true);
			alt.Inlines!.Add(italic);
			into.Children.Add(alt);
			return;
		}
		_figures++;
		var box = new Viewbox
		{
			Stretch = Stretch.Uniform,
			StretchDirection = StretchDirection.DownOnly,
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0, 4, 0, 12),
			Child = body,
		};
		AutomationProperties.SetName(box, PlainText(image));
		if (image.Title != null)
		{
			ToolTip.SetTip(box, image.Title);
		}
		into.Children.Add(box);
	}

	private Control? ResolveFigure(string source)
	{
		if (_options.FigureResolver != null)
		{
			return _options.FigureResolver(source);
		}
		if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
		{
			return null;
		}
		string name = Uri.UnescapeDataString(source).Replace('\\', '/');
		if (!name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return SvgFigure.FromResource(name)?.Root;
	}

	// ------------------------------------------------------------------ inlines

	private enum NodeKind { Root, Text, Code, SoftBreak, HardBreak, Emphasis, Strong, Link, Image }

	/// <summary>An inline node in a doubly linked list of siblings; Link and Image keep their destination in Text.</summary>
	private sealed class Node(NodeKind kind, string? text = null)
	{
		public readonly NodeKind Kind = kind;
		public string? Text = text;
		public string? Title;
		public Node? Parent;
		public Node? Previous;
		public Node? Next;
		public Node? First;
		public Node? Last;

		public void Append(Node child)
		{
			child.Parent = this;
			child.Previous = Last;
			child.Next = null;
			if (Last == null)
			{
				First = child;
			}
			else
			{
				Last.Next = child;
			}
			Last = child;
		}

		/// <summary>Puts this node right after <paramref name="sibling"/>, in its parent.</summary>
		public void InsertAfter(Node sibling)
		{
			Parent = sibling.Parent;
			Previous = sibling;
			Next = sibling.Next;
			if (sibling.Next == null)
			{
				Parent!.Last = this;
			}
			else
			{
				sibling.Next.Previous = this;
			}
			sibling.Next = this;
		}

		public void Unlink()
		{
			if (Previous == null)
			{
				Parent!.First = Next;
			}
			else
			{
				Previous.Next = Next;
			}
			if (Next == null)
			{
				Parent!.Last = Previous;
			}
			else
			{
				Next.Previous = Previous;
			}
			Parent = null;
			Previous = null;
			Next = null;
		}
	}

	/// <summary>A run of * or _ that may open or close emphasis, on CommonMark's delimiter stack.</summary>
	private sealed class Delimiter
	{
		public required Node Node;
		public char Char;
		public int Count;
		public int Original;
		public bool CanOpen;
		public bool CanClose;
		public Delimiter? Previous;
		public Delimiter? Next;
	}

	/// <summary>A [ or ![ waiting for its ], with the delimiter stack's top when it was met.</summary>
	private sealed class Bracket
	{
		public required Node Node;
		public bool Image;
		public bool Active = true;
		public Delimiter? Bottom;
	}

	/// <summary>CommonMark's inline parsing of one paragraph or heading: the nodes first, then the emphasis.</summary>
	private sealed class InlineParser(string s)
	{
		private readonly string _s = s;
		private readonly Node _root = new(NodeKind.Root);
		private readonly StringBuilder _text = new();
		private readonly List<Bracket> _brackets = [];
		private Delimiter? _top;
		private int _pos;

		public Node Parse()
		{
			while (_pos < _s.Length)
			{
				char c = _s[_pos];
				switch (c)
				{
					case '\\':
						Backslash();
						break;
					case '`':
						Backticks();
						break;
					case '*':
					case '_':
						Run(c);
						break;
					case '[':
						OpenBracket(false);
						break;
					case '!':
						if (_pos + 1 < _s.Length && _s[_pos + 1] == '[')
						{
							OpenBracket(true);
						}
						else
						{
							_text.Append(c);
							_pos++;
						}
						break;
					case ']':
						CloseBracket();
						break;
					case '<':
						Angle();
						break;
					case '&':
						Ampersand();
						break;
					case '\n':
						LineEnd();
						break;
					default:
						if (!Bare())
						{
							_text.Append(c);
							_pos++;
						}
						break;
				}
			}
			Flush();
			ProcessEmphasis(null);
			return _root;
		}

		private void Flush()
		{
			if (_text.Length > 0)
			{
				_root.Append(new Node(NodeKind.Text, _text.ToString()));
				_text.Clear();
			}
		}

		private Node Add(Node node)
		{
			Flush();
			_root.Append(node);
			return node;
		}

		private void AddLink(string destination, string text)
		{
			Add(new Node(NodeKind.Link, destination)).Append(new Node(NodeKind.Text, text));
		}

		private void Backslash()
		{
			char next = _pos + 1 < _s.Length ? _s[_pos + 1] : '\0';
			if (next == '\n')
			{
				Add(new Node(NodeKind.HardBreak));
				_pos += 2;
				SkipLeadingSpace();
			}
			else if (IsAsciiPunctuation(next))
			{
				_text.Append(next);
				_pos += 2;
			}
			else
			{
				_text.Append('\\');
				_pos++;
			}
		}

		private void Backticks()
		{
			int start = _pos;
			while (_pos < _s.Length && _s[_pos] == '`')
			{
				_pos++;
			}
			int count = _pos - start;
			for (int p = _pos; p < _s.Length;)
			{
				if (_s[p] != '`')
				{
					p++;
					continue;
				}
				int q = p;
				while (q < _s.Length && _s[q] == '`')
				{
					q++;
				}
				if (q - p == count)
				{
					string code = _s[_pos..p].Replace('\n', ' ');
					if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim(' ').Length > 0)
					{
						code = code[1..^1];
					}
					Add(new Node(NodeKind.Code, code));
					_pos = q;
					return;
				}
				p = q;
			}
			_text.Append('`', count);
		}

		// a delimiter run, left- or right-flanking by CommonMark's rules (_ not inside a word)
		private void Run(char c)
		{
			int start = _pos;
			while (_pos < _s.Length && _s[_pos] == c)
			{
				_pos++;
			}
			int count = _pos - start;
			char before = start > 0 ? _s[start - 1] : '\n', after = _pos < _s.Length ? _s[_pos] : '\n';
			bool beforeSpace = char.IsWhiteSpace(before), afterSpace = char.IsWhiteSpace(after);
			bool beforePunctuation = IsPunctuation(before), afterPunctuation = IsPunctuation(after);
			bool left = !afterSpace && (!afterPunctuation || beforeSpace || beforePunctuation);
			bool right = !beforeSpace && (!beforePunctuation || afterSpace || afterPunctuation);
			bool open = c == '*' ? left : left && (!right || beforePunctuation);
			bool close = c == '*' ? right : right && (!left || afterPunctuation);
			Node node = Add(new Node(NodeKind.Text, new string(c, count)));
			if (open || close)
			{
				var d = new Delimiter { Node = node, Char = c, Count = count, Original = count, CanOpen = open, CanClose = close, Previous = _top };
				if (_top != null)
				{
					_top.Next = d;
				}
				_top = d;
			}
		}

		private void Remove(Delimiter d)
		{
			if (d.Previous != null)
			{
				d.Previous.Next = d.Next;
			}
			if (d.Next != null)
			{
				d.Next.Previous = d.Previous;
			}
			else
			{
				_top = d.Previous;
			}
		}

		// CommonMark's "process emphasis" over the delimiters above bottom (null: all of them)
		private void ProcessEmphasis(Delimiter? bottom)
		{
			var openersBottom = new Delimiter?[12];
			var bottomSet = new bool[12];
			Delimiter? closer = null;
			for (Delimiter? d = _top; d != null && d != bottom; d = d.Previous)
			{
				closer = d;
			}
			while (closer != null)
			{
				if (!closer.CanClose)
				{
					closer = closer.Next;
					continue;
				}
				int key = (closer.Char == '*' ? 0 : 6) + (closer.CanOpen ? 3 : 0) + closer.Original % 3;
				Delimiter? opener = closer.Previous;
				bool found = false;
				while (opener != null && opener != bottom && !(bottomSet[key] && opener == openersBottom[key]))
				{
					bool multipleOfThree = (opener.CanClose || closer.CanOpen) && (opener.Original + closer.Original) % 3 == 0
						&& !(opener.Original % 3 == 0 && closer.Original % 3 == 0);
					if (opener.Char == closer.Char && opener.CanOpen && !multipleOfThree)
					{
						found = true;
						break;
					}
					opener = opener.Previous;
				}
				if (!found || opener == null)
				{
					openersBottom[key] = closer.Previous;
					bottomSet[key] = true;
					Delimiter? next = closer.Next;
					if (!closer.CanOpen)
					{
						Remove(closer);
					}
					closer = next;
					continue;
				}
				int use = closer.Count >= 2 && opener.Count >= 2 ? 2 : 1;
				opener.Count -= use;
				closer.Count -= use;
				opener.Node.Text = opener.Node.Text![use..];
				closer.Node.Text = closer.Node.Text![use..];
				var emphasis = new Node(use == 2 ? NodeKind.Strong : NodeKind.Emphasis);
				for (Node? n = opener.Node.Next; n != null && n != closer.Node;)
				{
					Node? next = n.Next;
					n.Unlink();
					emphasis.Append(n);
					n = next;
				}
				emphasis.InsertAfter(opener.Node);
				// the delimiters between the two are plain text now
				for (Delimiter? d = closer.Previous; d != null && d != opener;)
				{
					Delimiter? previous = d.Previous;
					Remove(d);
					d = previous;
				}
				if (opener.Count == 0)
				{
					opener.Node.Unlink();
					Remove(opener);
				}
				if (closer.Count == 0)
				{
					Delimiter? next = closer.Next;
					closer.Node.Unlink();
					Remove(closer);
					closer = next;
				}
			}
			while (_top != null && _top != bottom)
			{
				Remove(_top);
			}
		}

		private void OpenBracket(bool image)
		{
			Node node = Add(new Node(NodeKind.Text, image ? "![" : "["));
			_pos += image ? 2 : 1;
			_brackets.Add(new Bracket { Node = node, Image = image, Bottom = _top });
		}

		private void CloseBracket()
		{
			_pos++;
			Flush();
			Bracket? opener = _brackets.Count > 0 ? _brackets[^1] : null;
			if (opener == null || !opener.Active || !TryLinkTail(_pos, out string? destination, out string? title, out int end))
			{
				if (opener != null)
				{
					_brackets.RemoveAt(_brackets.Count - 1);
				}
				_root.Append(new Node(NodeKind.Text, "]"));
				return;
			}
			_brackets.RemoveAt(_brackets.Count - 1);
			_pos = end;
			var link = new Node(opener.Image ? NodeKind.Image : NodeKind.Link, destination) { Title = title };
			for (Node? n = opener.Node.Next; n != null;)
			{
				Node? next = n.Next;
				n.Unlink();
				link.Append(n);
				n = next;
			}
			link.InsertAfter(opener.Node);
			opener.Node.Unlink();
			ProcessEmphasis(opener.Bottom);
			if (!opener.Image)
			{
				// no link inside a link
				foreach (Bracket b in _brackets)
				{
					if (!b.Image)
					{
						b.Active = false;
					}
				}
			}
		}

		// an inline link's "(destination "title")" right after the ]
		private bool TryLinkTail(int p, out string destination, out string? title, out int end)
		{
			destination = "";
			title = null;
			end = p;
			if (p >= _s.Length || _s[p] != '(')
			{
				return false;
			}
			p = SkipSpace(p + 1);
			if (p < _s.Length && _s[p] == '<')
			{
				int q = p + 1;
				while (q < _s.Length && _s[q] != '>' && _s[q] != '<' && _s[q] != '\n')
				{
					q += _s[q] == '\\' && q + 1 < _s.Length ? 2 : 1;
				}
				if (q >= _s.Length || _s[q] != '>')
				{
					return false;
				}
				destination = Unescape(_s[(p + 1)..q]);
				p = q + 1;
			}
			else
			{
				int q = p, depth = 0;
				while (q < _s.Length)
				{
					char c = _s[q];
					if (c == '\\' && q + 1 < _s.Length && IsAsciiPunctuation(_s[q + 1]))
					{
						q += 2;
						continue;
					}
					if (c == '(')
					{
						depth++;
					}
					else if (c == ')')
					{
						if (depth == 0)
						{
							break;
						}
						depth--;
					}
					else if (char.IsWhiteSpace(c) || char.IsControl(c))
					{
						break;
					}
					q++;
				}
				if (depth != 0)
				{
					return false;
				}
				destination = Unescape(_s[p..q]);
				p = q;
			}
			int beforeSpace = p;
			p = SkipSpace(p);
			if (p > beforeSpace && p < _s.Length && (_s[p] == '"' || _s[p] == '\'' || _s[p] == '('))
			{
				char close = _s[p] == '(' ? ')' : _s[p];
				int q = p + 1;
				while (q < _s.Length && _s[q] != close)
				{
					q += _s[q] == '\\' && q + 1 < _s.Length ? 2 : 1;
				}
				if (q >= _s.Length)
				{
					return false;
				}
				title = Unescape(_s[(p + 1)..q]);
				p = SkipSpace(q + 1);
			}
			if (p >= _s.Length || _s[p] != ')')
			{
				return false;
			}
			end = p + 1;
			return true;
		}

		// spaces and tabs with at most one line end among them
		private int SkipSpace(int p)
		{
			bool lineEnd = false;
			while (p < _s.Length && (_s[p] == ' ' || _s[p] == '\t' || (_s[p] == '\n' && !lineEnd)))
			{
				lineEnd |= _s[p] == '\n';
				p++;
			}
			return p;
		}

		// an HTML comment (dropped), an autolink, or a plain <
		private void Angle()
		{
			if (string.CompareOrdinal(_s, _pos, "<!--", 0, 4) == 0)
			{
				int close = _s.IndexOf("-->", _pos + 4, StringComparison.Ordinal);
				if (close >= 0)
				{
					Flush();
					_pos = close + 3;
					return;
				}
			}
			Match match = AutoLink().Match(_s, _pos);
			if (match.Success)
			{
				AddLink(match.Groups[1].Value, match.Groups[1].Value);
				_pos += match.Length;
				return;
			}
			match = EmailLink().Match(_s, _pos);
			if (match.Success)
			{
				AddLink("mailto:" + match.Groups[1].Value, match.Groups[1].Value);
				_pos += match.Length;
				return;
			}
			_text.Append('<');
			_pos++;
		}

		private void Ampersand()
		{
			Match match = Entity().Match(_s, _pos);
			string? decoded = match.Success ? Decode(match) : null;
			if (decoded != null)
			{
				_text.Append(decoded);
				_pos += match.Length;
			}
			else
			{
				_text.Append('&');
				_pos++;
			}
		}

		// a line end: a hard break after two spaces or more, else a soft one; the next line's indentation goes
		private void LineEnd()
		{
			int spaces = 0;
			while (_text.Length > 0 && _text[^1] == ' ')
			{
				_text.Length--;
				spaces++;
			}
			Add(new Node(spaces >= 2 ? NodeKind.HardBreak : NodeKind.SoftBreak));
			_pos++;
			SkipLeadingSpace();
		}

		private void SkipLeadingSpace()
		{
			while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t'))
			{
				_pos++;
			}
		}

		// GitHub's bare URL at the start of a word: http://, https:// or www., trailing punctuation left out
		private bool Bare()
		{
			char c = _s[_pos];
			if (c is not ('h' or 'H' or 'w' or 'W'))
			{
				return false;
			}
			char before = _pos > 0 ? _s[_pos - 1] : '\n';
			if (!char.IsWhiteSpace(before) && before is not ('*' or '_' or '~' or '('))
			{
				return false;
			}
			Match match = BareUrl().Match(_s, _pos);
			if (!match.Success)
			{
				return false;
			}
			string url = match.Value;
			while (url.Length > 0)
			{
				char last = url[^1];
				if ("?!.,:*_~'\"".Contains(last) || (last == ')' && url.Count(ch => ch == '(') < url.Count(ch => ch == ')')))
				{
					url = url[..^1];
				}
				else if (last == ';' && TrailingEntity().IsMatch(url))
				{
					url = url[..url.LastIndexOf('&')];
				}
				else
				{
					break;
				}
			}
			bool www = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
			string host = url[(www ? 4 : url.IndexOf("://", StringComparison.Ordinal) + 3)..];
			int stop = host.IndexOfAny(['/', '?', '#']);
			if (stop >= 0)
			{
				host = host[..stop];
			}
			if (host.Length == 0 || (www && !host.Contains('.')) || !Host().IsMatch(host))
			{
				return false;
			}
			AddLink(www ? "http://" + url : url, url);
			_pos += url.Length;
			return true;
		}

		private static string Unescape(string s)
		{
			var sb = new StringBuilder();
			for (int i = 0; i < s.Length; i++)
			{
				if (s[i] == '\\' && i + 1 < s.Length && IsAsciiPunctuation(s[i + 1]))
				{
					sb.Append(s[++i]);
					continue;
				}
				if (s[i] == '&')
				{
					Match match = Entity().Match(s, i);
					string? decoded = match.Success ? Decode(match) : null;
					if (decoded != null)
					{
						sb.Append(decoded);
						i += match.Length - 1;
						continue;
					}
				}
				sb.Append(s[i]);
			}
			return sb.ToString();
		}

		private static string? Decode(Match match)
		{
			int code;
			if (match.Groups[1].Success)
			{
				code = int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			}
			else if (match.Groups[2].Success)
			{
				code = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
			}
			else
			{
				return Entities.TryGetValue(match.Groups[3].Value, out string? named) ? named : null;
			}
			if (code == 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
			{
				code = 0xFFFD;
			}
			return char.ConvertFromUtf32(code);
		}

		private static bool IsAsciiPunctuation(char c) => c < 128 && "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~".Contains(c);

		// CommonMark's punctuation: Unicode's punctuation and symbols
		private static bool IsPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c);
	}

	// ------------------------------------------------------------------ helpers

	private static string PlainText(Node parent)
	{
		var sb = new StringBuilder();
		for (Node? n = parent.First; n != null; n = n.Next)
		{
			if (n.Kind is NodeKind.Text or NodeKind.Code)
			{
				sb.Append(n.Text);
			}
			else if (n.Kind is NodeKind.SoftBreak or NodeKind.HardBreak)
			{
				sb.Append(' ');
			}
			else
			{
				sb.Append(PlainText(n));
			}
		}
		return sb.ToString();
	}

	private static bool IsBlank(string? line) => string.IsNullOrWhiteSpace(line);

	private static int Indent(string line)
	{
		int i = 0;
		while (i < line.Length && line[i] == ' ')
		{
			i++;
		}
		return i;
	}

	private static string ExpandTabs(string line)
	{
		if (!line.Contains('\t'))
		{
			return line;
		}
		var sb = new StringBuilder();
		foreach (char c in line)
		{
			if (c == '\t')
			{
				sb.Append(' ', 4 - sb.Length % 4);
			}
			else
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using Emoji.Wpf;
using ICSharpCode.AvalonEdit.Utils;
using Typography.TextLayout;

namespace ICSharpCode.AvalonEdit.Rendering
{
	// This class is internal because it does not need to be accessed by the user - it can be configured using TextEditorOptions.

	/// <summary>
	/// Element generator that displays colored emoji characters.
	/// </summary>
	/// <remarks>
	/// This element generator is present in every TextView by default; the enabled features can be configured using the
	/// <see cref="TextEditorOptions"/>.
	/// </remarks>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1702:CompoundWordsShouldBeCasedCorrectly", MessageId = "Whitespace")]
	sealed class ColorFontElementGenerator : VisualLineElementGenerator, IBuiltinElementGenerator
	{
		public override VisualLineElement ConstructElement(int offset)
		{
			EmojiTypeface emojiTypeface;
			string value;
			TextLine textLine;
			int matchOffset = GetColorGlyphOffset(offset, out emojiTypeface, out value, out textLine);
			if (emojiTypeface != null && matchOffset == offset)
			{
				return new ColorFontElement(value, emojiTypeface, textLine);
			}
			else
			{
				return null;
			}
		}

		public override int GetFirstInterestedOffset(int startOffset)
		{
			return GetColorGlyphOffset(startOffset, out _, out _, out _);
		}

		void IBuiltinElementGenerator.FetchOptions(TextEditorOptions options)
		{
		}

		static IDictionary<GlyphTypeface, EmojiTypeface> emojiTypefaceCache = new Dictionary<GlyphTypeface, EmojiTypeface>();
		Lazy<TextFormatter> formatter;
		TextLineCache previousTextLine;

		public ColorFontElementGenerator()
		{
			formatter = new Lazy<TextFormatter>(() => TextFormatter.Create(TextOptions.GetTextFormattingMode(CurrentContext.TextView)));
		}

		int GetColorGlyphOffset(int startOffset, out EmojiTypeface emojiTypeface, out string value, out TextLine textLine)
		{
			int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
			StringSegment relevantText = CurrentContext.GetText(startOffset, endOffset - startOffset);
			textLine = previousTextLine.Text == relevantText.Text ?
				previousTextLine.TextLine : FormattedTextElement.PrepareText(formatter.Value, relevantText.Text, CurrentContext.GlobalTextRunProperties);
			previousTextLine = new TextLineCache {Text = relevantText.Text, TextLine = textLine};

			foreach (IndexedGlyphRun indexedGlyphRun in textLine.GetIndexedGlyphRuns()
				.Where(x => x.TextSourceCharacterIndex >= relevantText.Offset && x.TextSourceCharacterIndex + x.TextSourceLength <= relevantText.Offset + relevantText.Count ||
				            x.TextSourceCharacterIndex < relevantText.Offset && x.TextSourceCharacterIndex + x.TextSourceLength > relevantText.Offset))
			{
				GlyphTypeface glyphTypeface = indexedGlyphRun.GlyphRun.GlyphTypeface;
				lock (emojiTypefaceCache)
				{
					if (!emojiTypefaceCache.TryGetValue(glyphTypeface, out emojiTypeface))
					{
						using (Stream stream = glyphTypeface.GetFontStream())
						{
							Typography.OpenFont.OpenFontReader reader = new Typography.OpenFont.OpenFontReader();
							Typography.OpenFont.Typeface typeface = reader.Read(stream);

							emojiTypefaceCache[glyphTypeface] = emojiTypeface = (typeface != null && typeface.COLRTable != null && typeface.CPALTable != null ? new EmojiTypeface(typeface.Filename) : null);
						}
					}
				}

				if (emojiTypeface != null)
				{
					int characterIndex = indexedGlyphRun.TextSourceCharacterIndex - relevantText.Offset;
					int result = characterIndex >= 0 ? startOffset + characterIndex : startOffset;
					string characters = characterIndex >= 0 ? relevantText.Text.Substring(indexedGlyphRun.TextSourceCharacterIndex, relevantText.Count - characterIndex) : relevantText.Text.Substring(relevantText.Offset, relevantText.Count);
					GlyphPlanSequence glyphPlanSequence = emojiTypeface.MakeGlyphPlanSequence(characters);
					if (glyphPlanSequence.Count > 1)
					{
						StringInfo textElements = new StringInfo(characters);
						StringBuilder sb = new StringBuilder();
						for (int i = 0; i < glyphPlanSequence.Count; i++)
						{
							int currentCpOffset = glyphPlanSequence[i].input_cp_offset;
							int nextCpOffset = i + 1 < glyphPlanSequence.Count ? glyphPlanSequence[i + 1].input_cp_offset : textElements.LengthInTextElements;
							sb.Append(textElements.SubstringByTextElements(currentCpOffset, nextCpOffset - currentCpOffset));
							if (textElements.SubstringByTextElements(nextCpOffset - 1, 1) != "\u200D"
							    && (nextCpOffset >= textElements.LengthInTextElements || textElements.SubstringByTextElements(nextCpOffset, 1) != "\u200D"))
								break;
						}
						value = sb.ToString();
					}
					else
					{
						value = glyphPlanSequence.Count > 0 ? characters : string.Empty;
					}
					return result;
				}
			}
			emojiTypeface = null;
			value = null;
			return -1;
		}

		struct TextLineCache
		{
			public string Text { get; set; }
			public TextLine TextLine { get; set; }
		}
	}

	sealed class ColorFontElement : VisualLineElement
	{
		internal string Value { get; private set; }
		internal EmojiTypeface EmojiTypeface { get; private set; }
		internal GlyphPlanSequence GlyphPlanSequence { get; private set; }
		internal TextLine TextLine { get; private set; }

		public ColorFontElement(string value, EmojiTypeface emojiTypeface, TextLine textLine) : base(1, value.Length)
		{
			Value = value;
			EmojiTypeface = emojiTypeface;
			TextLine = textLine;
			GlyphPlanSequence = emojiTypeface.MakeGlyphPlanSequence(value ?? string.Empty);
		}

		public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
		{
			return new ColorFontGlyphRun(this, TextRunProperties, context);
		}
	}

	sealed class ColorFontGlyphRun : TextEmbeddedObject
	{
		ColorFontElement element;
		TextRunProperties properties;
		ITextRunConstructionContext context;

		public override LineBreakCondition BreakBefore
		{
			get { return LineBreakCondition.BreakPossible; }
		}

		public override LineBreakCondition BreakAfter
		{
			get { return LineBreakCondition.BreakDesired; }
		}

		public override bool HasFixedSize
		{
			get { return true; }
		}

		public override CharacterBufferReference CharacterBufferReference
		{
			get { return new CharacterBufferReference(); }
		}

		public override int Length
		{
			get { return 1; }
		}

		public override TextRunProperties Properties
		{
			get { return properties; }
		}

		public ColorFontGlyphRun(ColorFontElement element, TextRunProperties properties, ITextRunConstructionContext context)
		{
			if (properties == null)
				throw new ArgumentNullException("properties");
			this.element = element;
			this.properties = properties;
			this.context = context;
		}

		public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways)
		{
			EmojiTypeface typeface = element.EmojiTypeface;
			GlyphPlanSequence glyphPlanSequence = typeface.MakeGlyphPlanSequence(element.Value ?? "");
			double fontSize = this.Properties.FontRenderingEmSize;
			double scale = typeface.GetScale(fontSize) * 0.75;
			double width = (double)glyphPlanSequence.CalculateWidth() * scale;

			return new Rect(0, 0, width, fontSize);
		}

		public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
		{
			EmojiTypeface typeface = element.EmojiTypeface;
			GlyphPlanSequence glyphPlanSequence = element.GlyphPlanSequence;
			double fontSize = Properties.FontRenderingEmSize;
			double scale = typeface.GetScale(fontSize) * 0.75;
			if (glyphPlanSequence.Count > 0 && fontSize > 0.0) {
				double x = origin.X;
				double y = origin.Y;
				for (int i = 0; i < glyphPlanSequence.Count; i++) {
					UnscaledGlyphPlan unscaledGlyphPlan = glyphPlanSequence[i];
					typeface.RenderGlyph(
						drawingContext,
						unscaledGlyphPlan.glyphIndex,
						new Point(x + unscaledGlyphPlan.OffsetX * scale, y + unscaledGlyphPlan.OffsetY * scale),
						fontSize,
						Properties.ForegroundBrush);
					x += unscaledGlyphPlan.AdvanceX * scale;
				}
			}
		}

		public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth)
		{
			EmojiTypeface typeface = element.EmojiTypeface;
			GlyphPlanSequence glyphPlanSequence = element.GlyphPlanSequence;
			double fontSize = this.Properties.FontRenderingEmSize;
			double scale = typeface.GetScale(fontSize) * 0.75;
			double width = glyphPlanSequence.CalculateWidth() * scale;

			return new TextEmbeddedObjectMetrics(width, element.TextLine.Height, element.TextLine.Baseline);
		}
	}
}

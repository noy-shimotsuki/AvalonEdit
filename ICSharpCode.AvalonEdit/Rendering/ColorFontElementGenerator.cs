using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
			int matchOffset = GetColorGlyphOffset(offset, out emojiTypeface, out value);
			if (emojiTypeface != null && matchOffset == offset)
			{
				return new ColorFontElement(value, emojiTypeface);
			}
			else
			{
				return null;
			}
		}

		public override int GetFirstInterestedOffset(int startOffset)
		{
			return GetColorGlyphOffset(startOffset, out _, out _);
		}

		void IBuiltinElementGenerator.FetchOptions(TextEditorOptions options)
		{
		}

		static IDictionary<GlyphTypeface, EmojiTypeface> emojiTypefaceCache = new Dictionary<GlyphTypeface, EmojiTypeface>();

		int GetColorGlyphOffset(int startOffset, out EmojiTypeface emojiTypeface, out string value)
		{
			int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
			StringSegment relevantText = CurrentContext.GetText(startOffset, endOffset - startOffset);
			TextFormatter formatter = TextFormatter.Create(TextOptions.GetTextFormattingMode(CurrentContext.TextView));
			TextLine textLine = FormattedTextElement.PrepareText(formatter, relevantText.Text.Substring(relevantText.Offset, relevantText.Count), CurrentContext.GlobalTextRunProperties);
			foreach (IndexedGlyphRun indexedGlyphRun in textLine.GetIndexedGlyphRuns())
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
					int result = startOffset + indexedGlyphRun.TextSourceCharacterIndex;
					string characters = relevantText.Text.Substring(relevantText.Offset + indexedGlyphRun.TextSourceCharacterIndex, relevantText.Count - indexedGlyphRun.TextSourceCharacterIndex);
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
	}

	sealed class ColorFontElement : VisualLineElement
	{
		internal string Value { get; private set; }
		internal EmojiTypeface EmojiTypeface { get; set; }

		public ColorFontElement(string value, EmojiTypeface emojiTypeface) : base(1, value.Length)
		{
			Value = value;
			EmojiTypeface = emojiTypeface;
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
			GlyphPlanSequence glyphPlanSequence = typeface.MakeGlyphPlanSequence(element.Value ?? string.Empty);
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
			var glyphPlanSequence = typeface.MakeGlyphPlanSequence(element.Value ?? "");
			var fontSize = this.Properties.FontRenderingEmSize;
			var scale = typeface.GetScale(fontSize) * 0.75;
			var width = glyphPlanSequence.CalculateWidth() * scale;

			TextFormatter formatter = TextFormatter.Create(TextOptions.GetTextFormattingMode(context.TextView));
			TextLine textLine = FormattedTextElement.PrepareText(formatter, element.Value, Properties);
			return new TextEmbeddedObjectMetrics(width, textLine.Height, textLine.Baseline);
		}
	}
}

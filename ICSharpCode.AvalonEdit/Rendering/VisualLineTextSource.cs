﻿// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics;
using System.Windows.Media.TextFormatting;

using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Utils;

namespace ICSharpCode.AvalonEdit.Rendering
{
	/// <summary>
	/// WPF TextSource implementation that creates TextRuns for a VisualLine.
	/// </summary>
	sealed class VisualLineTextSource : TextSource, ITextRunConstructionContext
	{
		public VisualLineTextSource(VisualLine visualLine)
		{
			this.VisualLine = visualLine;
		}

		public VisualLine VisualLine { get; private set; }
		public TextView TextView { get; set; }
		public TextDocument Document { get; set; }
		public TextRunProperties GlobalTextRunProperties { get; set; }

		public override TextRun GetTextRun(int textSourceCharacterIndex)
		{
			try {
				foreach (VisualLineElement element in VisualLine.Elements) {
					if (textSourceCharacterIndex >= element.VisualColumn
						&& textSourceCharacterIndex < element.VisualColumn + element.VisualLength) {
						int relativeOffset = textSourceCharacterIndex - element.VisualColumn;
						TextRun run = element.CreateTextRun(textSourceCharacterIndex, this);
						if (run == null)
							throw new ArgumentNullException(element.GetType().Name + ".CreateTextRun");
						if (run.Length == 0)
							throw new ArgumentException("The returned TextRun must not have length 0.", element.GetType().Name + ".Length");
						if (relativeOffset + run.Length > element.VisualLength)
							throw new ArgumentException("The returned TextRun is too long.", element.GetType().Name + ".CreateTextRun");
						InlineObjectRun inlineRun = run as InlineObjectRun;
						if (inlineRun != null) {
							inlineRun.VisualLine = VisualLine;
							VisualLine.hasInlineObjects = true;
							TextView.AddInlineObject(inlineRun);
						}
						return run;
					}
				}
				int delimiterLength = VisualLine.LastDocumentLine.DelimiterLength;
				if ((TextView.Options.ShowEndOfLine && delimiterLength > 0 || TextView.Options.ShowEndOfFile && delimiterLength == 0) && textSourceCharacterIndex == VisualLine.VisualLength) {
					return CreateTextRunForNewLine();
				}
				return new TextEndOfParagraph(1);
			} catch (Exception ex) {
				Debug.WriteLine(ex.ToString());
				throw;
			}
		}

		TextRun CreateTextRunForNewLine()
		{
			string newlineText = "";
			DocumentLine lastDocumentLine = VisualLine.LastDocumentLine;
			if (lastDocumentLine.DelimiterLength == 2) {
				newlineText = TextView.Options.EndOfLineCrLfText;
			} else if (lastDocumentLine.DelimiterLength == 1) {
				char newlineChar = Document.GetCharAt(lastDocumentLine.Offset + lastDocumentLine.Length);
				if (newlineChar == '\r')
					newlineText = TextView.Options.EndOfLineCrText;
				else if (newlineChar == '\n')
					newlineText = TextView.Options.EndOfLineLfText;
				else
					newlineText = "?";
			} else if (lastDocumentLine.DelimiterLength == 0) {
				newlineText = TextView.Options.EndOfFileText;
			}
			return new EndOfLineTextRun(new FormattedTextElement(TextView.cachedElements.GetTextForNonPrintableCharacter(newlineText, this), 0), GlobalTextRunProperties);
		}

		public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
		{
			try {
				foreach (VisualLineElement element in VisualLine.Elements) {
					if (textSourceCharacterIndexLimit > element.VisualColumn
						&& textSourceCharacterIndexLimit <= element.VisualColumn + element.VisualLength) {
						TextSpan<CultureSpecificCharacterBufferRange> span = element.GetPrecedingText(textSourceCharacterIndexLimit, this);
						if (span == null)
							break;
						int relativeOffset = textSourceCharacterIndexLimit - element.VisualColumn;
						if (span.Length > relativeOffset)
							throw new ArgumentException("The returned TextSpan is too long.", element.GetType().Name + ".GetPrecedingText");
						return span;
					}
				}
				CharacterBufferRange empty = CharacterBufferRange.Empty;
				return new TextSpan<CultureSpecificCharacterBufferRange>(empty.Length, new CultureSpecificCharacterBufferRange(null, empty));
			} catch (Exception ex) {
				Debug.WriteLine(ex.ToString());
				throw;
			}
		}

		public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex)
		{
			throw new NotSupportedException();
		}

		string cachedString;
		int cachedStringOffset;

		public StringSegment GetText(int offset, int length)
		{
			if (cachedString != null) {
				if (offset >= cachedStringOffset && offset + length <= cachedStringOffset + cachedString.Length) {
					return new StringSegment(cachedString, offset - cachedStringOffset, length);
				}
			}
			cachedStringOffset = offset;
			return new StringSegment(cachedString = this.Document.GetText(offset, length));
		}

		sealed class EndOfLineTextRun : FormattedTextRun
		{
			public EndOfLineTextRun(FormattedTextElement element, TextRunProperties properties)
				: base(element, properties)
			{
			}

			public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth)
			{
				TextEmbeddedObjectMetrics metrics = base.Format(remainingParagraphWidth);
				return new TextEmbeddedObjectMetrics(0, metrics.Height, metrics.Baseline);
			}
		}
	}
}

﻿// SPDX-License-Identifier: MIT

using System;
using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using NUnit.Framework;

namespace ICSharpCode.AvalonEdit.Highlighting
{
	[TestFixture]
	public class HtmlClipboardTests
	{
		TextDocument document;
		DocumentHighlighter highlighter;
		
		public HtmlClipboardTests()
		{
			document = new TextDocument("using System.Text;\n\tstring text = SomeMethod();");
			highlighter = new DocumentHighlighter(document, HighlightingManager.Instance.GetDefinition("C#"));
		}
		
		[Test]
		public void FullDocumentTest()
		{
			var segment = new TextSegment { StartOffset = 0, Length = document.TextLength };
			string html = HtmlClipboard.CreateHtmlFragment(document, highlighter, segment, new HtmlOptions());
			Assert.AreEqual("<span style=\"color: #008000; font-weight: bold; \">using</span> System.Text;<br>" + Environment.NewLine +
			                "&nbsp;&nbsp;&nbsp;&nbsp;<span style=\"color: #ff0000; \">string</span> " +
			                "text = <span style=\"color: #191970; font-weight: bold; \">SomeMethod</span>();", html);
		}
		
		[Test]
		public void PartOfHighlightedWordTest()
		{
			var segment = new TextSegment { StartOffset = 1, Length = 3 };
			string html = HtmlClipboard.CreateHtmlFragment(document, highlighter, segment, new HtmlOptions());
			Assert.AreEqual("<span style=\"color: #008000; font-weight: bold; \">sin</span>", html);
		}
	}
}

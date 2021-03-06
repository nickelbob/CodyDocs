﻿using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CodyDocs.EditorUI.DocumentedCodeHighlighter
{
    public class DocumentationTag : IGlyphTag
    {
        public string DocumentationFragmentText { get; private set; }
        public ITrackingSpan TrackingSpan { get; set; }
        public ITextBuffer TextBuffer { get; set; }

        public DocumentationTag(string fragment, ITrackingSpan trackingSpan, ITextBuffer buffer)
        {
            DocumentationFragmentText = fragment;
            TrackingSpan = trackingSpan;
            TextBuffer = buffer;
        }

    }
}

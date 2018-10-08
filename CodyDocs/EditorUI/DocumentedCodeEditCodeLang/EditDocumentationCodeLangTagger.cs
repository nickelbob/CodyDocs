using System;
using System.Collections.Generic;
using System.Linq;
using CodyDocs.EditorUI.DocumentedCodeHighlighter;
using CodyDocs.Utils;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CodyDocs.EditorUI.DocumentedCodeEditCodeLang
{

    internal sealed class EditDocumentationCodeLangTagger : ITagger<DocumentationTag>
    {
        private ITagAggregator<DocumentationTag> _tagAggregator;
        private ITextBuffer _buffer;
        private IWpfTextView _view;
        private string _codyDocsFilename;
        private ITextSnapshot snapshot { get; }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public EditDocumentationCodeLangTagger(IWpfTextView view, ITagAggregator<DocumentationTag> tagAggregator)
        {
            this._tagAggregator = tagAggregator;
            snapshot = view.TextBuffer.CurrentSnapshot;
            _tagAggregator.TagsChanged += OnTagsChanged;
            _buffer = view.TextBuffer;
            _view = view;
            _codyDocsFilename = view.TextBuffer.GetCodyDocsFileName();
            

        }

        private void OnTagsChanged(object sender, TagsChangedEventArgs e)
        {
            var snapshotSpan = e.Span.GetSnapshotSpan();//Extension method
            InvokeTagsChanged(sender, new SnapshotSpanEventArgs(snapshotSpan));
        }

        protected void InvokeTagsChanged(object sender, SnapshotSpanEventArgs args)
        {
            TagsChanged?.Invoke(sender, args);
        }

        public void Dispose()
        {
            _tagAggregator.Dispose();

            //view.Properties.RemoveProperty(typeof(EditDocumentationAdornmentTagger));
        }

        // Produces tags on the snapshot that the tag consumer asked for.
        public IEnumerable<ITagSpan<DocumentationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            var documentation = Services.DocumentationFileSerializer.Deserialize(_codyDocsFilename);

            List<ITagSpan<DocumentationTag>> res =
                new List<ITagSpan<DocumentationTag>>();
            var currentSnapshot = _buffer.CurrentSnapshot;


            var hiddenTextManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsHiddenTextManager;
            var service = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));
            var textManager = service as IVsTextManager2;
            IVsTextView view;
            int result = textManager.GetActiveView2(1, null, (uint)_VIEWFRAMETYPE.vftCodeWindow, out view);
            IVsHiddenTextSession hiddenSession = null;
            IVsEnumHiddenRegions[] hiddenRegions = null;
            int hRetVal = hiddenTextManager.GetHiddenTextSession(
                             _buffer,
                             out hiddenSession);
            if (hRetVal != 0)
            {
                hRetVal = hiddenTextManager.CreateHiddenTextSession(
                             0,
                             _buffer,
                             null,
                             out hiddenSession);
            }

            if (hiddenSession != null)
            {
                foreach (var fragment in documentation.Fragments)
                {
                    int startPos = fragment.Selection.StartPosition;
                    int length = fragment.Selection.EndPosition - fragment.Selection.StartPosition;
                    var snapshotSpan = new SnapshotSpan(
                         currentSnapshot, new Span(startPos, length));

                    view.GetLineAndColumn(fragment.Selection.StartPosition, out int startLine, out int startIdx);
                    view.GetLineAndColumn(fragment.Selection.EndPosition, out int endLine, out int endIdx);

                    var hidRegion = new NewHiddenRegion()
                    {
                        dwBehavior = (uint)HIDDEN_REGION_BEHAVIOR.hrbClientControlled,
                        dwState = (uint)HIDDEN_REGION_STATE.hrsDefault,
                        iType = (int)HIDDEN_REGION_TYPE.hrtConcealed,
                        pszBanner = fragment.Documentation,
                        tsHiddenText = new TextSpan()
                        {
                            iStartLine = startLine,
                            iStartIndex = startIdx,
                            iEndLine = endLine,
                            iEndIndex = endIdx
                        }
                    };
                    hiddenSession.AddHiddenRegions(0, 1, new[] { hidRegion }, hiddenRegions);

                    res.Add(new TagSpan<DocumentationTag>(snapshotSpan, new DocumentationTag(fragment.Selection.Text, snapshot.CreateTrackingSpan(startPos, length, SpanTrackingMode.EdgeInclusive), _buffer)));
                }
            }

            return res;
        }
    }

}

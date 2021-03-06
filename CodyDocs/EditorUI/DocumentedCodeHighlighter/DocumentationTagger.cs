﻿using CodyDocs.Events;
using CodyDocs.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using System;
using System.Linq;
using System.Collections.Generic;
using CodyDocs.Models;
using CodyDocs.Utils;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CodyDocs.EditorUI.DocumentedCodeHighlighter
{
    class DocumentationTagger : ITagger<DocumentationTag>
    {
        private ITextView _textView;
        private ITextBuffer _buffer;
        private IEventAggregator _eventAggregator;
        private readonly DelegateListener<DocumentationAddedEvent> _documentationAddedListener;
        private DelegateListener<DocumentSavedEvent> _documentSavedListener;
        private DelegateListener<DocumentationUpdatedEvent> _documentationUpdatedListener;
        private readonly DelegateListener<DocumentClosedEvent> _documentatiClosedListener;
        private readonly string _filename;
        private readonly string _codyDocsFilename;
        private readonly ITextSnapshot snapshot;
        private readonly DelegateListener<DocumentationDeletedEvent> _documentationDeletedListener;

        private string CodyDocsFilename { get { return _filename + Consts.CODY_DOCS_EXTENSION; } }

        /// <summary>
        /// Key is the tracking span. Value is the documentation for that span.
        /// </summary>
        Dictionary<ITrackingSpan, string> _trackingSpans;


        public DocumentationTagger(ITextView textView, ITextBuffer buffer, IEventAggregator eventAggregator)
        {
            _textView = textView;
            _buffer = buffer;
            _eventAggregator = eventAggregator;
            _filename = buffer.GetFileName();
            _codyDocsFilename = textView.TextBuffer.GetCodyDocsFileName();
            snapshot = textView.TextBuffer.CurrentSnapshot;

            _documentationAddedListener = new DelegateListener<DocumentationAddedEvent>(OnDocumentationAdded);
            _eventAggregator.AddListener<DocumentationAddedEvent>(_documentationAddedListener);
            _documentSavedListener = new DelegateListener<DocumentSavedEvent>(OnDocumentSaved);
            _eventAggregator.AddListener<DocumentSavedEvent>(_documentSavedListener);
            _documentatiClosedListener = new DelegateListener<DocumentClosedEvent>(OnDocumentatClosed);
            _eventAggregator.AddListener<DocumentClosedEvent>(_documentatiClosedListener);
            _documentationUpdatedListener = new DelegateListener<DocumentationUpdatedEvent>(OnDocumentationUpdated);
            _eventAggregator.AddListener<DocumentationUpdatedEvent>(_documentationUpdatedListener);
            _documentationDeletedListener = new DelegateListener<DocumentationDeletedEvent>(OnDocumentationDeleted);
            _eventAggregator.AddListener<DocumentationDeletedEvent>(_documentationDeletedListener);


            //TODO: Add event aggregator listener to documentation changed on tracking Span X
            //TODO: Add event aggregator listener to documentation deleted on tracking Span X

            CreateTrackingSpans();

        }


        private void OnDocumentatClosed(DocumentClosedEvent obj)
        {
            if (obj.DocumentFullName == _filename)
            {
                _eventAggregator.RemoveListener(_documentationAddedListener);
                _eventAggregator.RemoveListener(_documentSavedListener);
                _eventAggregator.RemoveListener(_documentationUpdatedListener);
                _eventAggregator.RemoveListener(_documentatiClosedListener);
            }

        }

        private void CreateTrackingSpans()
        {
            _trackingSpans = new Dictionary<ITrackingSpan, string>();
            var documentation = Services.DocumentationFileSerializer.Deserialize(CodyDocsFilename);
            var currentSnapshot = _buffer.CurrentSnapshot;
            foreach (var fragment in documentation.Fragments)
            {
                Span span = fragment.GetSpan();
                var trackingSpan = currentSnapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeExclusive);
                _trackingSpans.Add(trackingSpan, fragment.Documentation);
            }
        }

        private void OnDocumentSaved(DocumentSavedEvent documentSavedEvent)
        {
            if (documentSavedEvent.DocumentFullName == _filename)
            {
                RemoveEmptyTrackingSpans();
                FileDocumentation fileDocumentation = CreateFileDocumentationFromTrackingSpans();
                DocumentationFileSerializer.Serialize(CodyDocsFilename, fileDocumentation);
            }
        }

        private void OnDocumentationUpdated(DocumentationUpdatedEvent ev)
        {
            if (_trackingSpans.ContainsKey(ev.TrackingSpan))
            {
                _trackingSpans[ev.TrackingSpan] = ev.NewDocumentation;
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(_buffer.CurrentSnapshot, ev.TrackingSpan.GetSpan(_buffer.CurrentSnapshot))));
                MarkDocumentAsUnsaved();
            }
        }

        private void OnDocumentationDeleted(DocumentationDeletedEvent ev)
        {
            if (_trackingSpans.ContainsKey(ev.Tag.TrackingSpan))
            {
                _trackingSpans.Remove(ev.Tag.TrackingSpan);
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(_buffer.CurrentSnapshot, ev.Tag.TrackingSpan.GetSpan(_buffer.CurrentSnapshot))));
                MarkDocumentAsUnsaved();
            }
        }


        private void MarkDocumentAsUnsaved()
        {
            Document document = DocumentLifetimeManager.GetDocument(_filename);
            if (document != null)
                document.Saved = false;
        }

        private void RemoveEmptyTrackingSpans()
        {
            var currentSnapshot = _buffer.CurrentSnapshot;
            var keysToRemove = _trackingSpans.Keys.Where(ts => ts.GetSpan(currentSnapshot).Length == 0).ToList();
            foreach (var key in keysToRemove)
            {
                _trackingSpans.Remove(key);
            }
        }

        private FileDocumentation CreateFileDocumentationFromTrackingSpans()
        {
            var currentSnapshot = _buffer.CurrentSnapshot;
            List<DocumentationFragment> fragments = _trackingSpans
                .Select(ts => new DocumentationFragment()
                {
                    Selection = new TextViewSelection()
                    {
                        StartPosition = ts.Key.GetStartPoint(currentSnapshot),
                        EndPosition = ts.Key.GetEndPoint(currentSnapshot),
                        Text = ts.Key.GetText(currentSnapshot)
                    },
                    Documentation = ts.Value,

                }).ToList();

            var fileDocumentation = new FileDocumentation() { Fragments = fragments };
            return fileDocumentation;
        }

        private void OnDocumentationAdded(DocumentationAddedEvent e)
        {

            string filepath = e.Filepath;
            if (filepath == CodyDocsFilename)
            {
                var span = e.DocumentationFragment.GetSpan();
                var trackingSpan = _buffer.CurrentSnapshot.CreateTrackingSpan(span, SpanTrackingMode.EdgeExclusive);
                _trackingSpans.Add(trackingSpan, e.DocumentationFragment.Documentation);
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                    new SnapshotSpan(_buffer.CurrentSnapshot, span)));
                MarkDocumentAsUnsaved();
            }
        }


        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        //public IEnumerable<ITagSpan<DocumentationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        //{
        //    List<ITagSpan<DocumentationTag>> tags = new List<ITagSpan<DocumentationTag>>();
        //    if (spans.Count == 0)
        //        return tags;

        //    var relevantSnapshot = spans.First().Snapshot;//_buffer.CurrentSnapshot;
        //    foreach (var trackingSpan in _trackingSpans.Keys)
        //    {
        //        var spanInCurrentSnapshot = trackingSpan.GetSpan(relevantSnapshot);
        //        if (spans.Any(sp => spanInCurrentSnapshot.IntersectsWith(sp)))
        //        {
        //            var snapshotSpan = new SnapshotSpan(relevantSnapshot, spanInCurrentSnapshot);
        //            var documentationText = _trackingSpans[trackingSpan];
        //            tags.Add(new TagSpan<DocumentationTag>(snapshotSpan,
        //                new DocumentationTag(documentationText, trackingSpan, _buffer)));
        //        }

        //    }
        //    return tags;
        //}

        // Produces tags on the snapshot that the tag consumer asked for.
        public IEnumerable<ITagSpan<DocumentationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            List<ITagSpan<DocumentationTag>> tags = new List<ITagSpan<DocumentationTag>>();
            if (spans.Count == 0)
                return tags;

            var documentation = Services.DocumentationFileSerializer.Deserialize(_codyDocsFilename);

            var currentSnapshot = _buffer.CurrentSnapshot;


            var hiddenTextManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsHiddenTextManager;
            var service = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));
            var textManager = service as IVsTextManager2;
            IVsTextView view;
            int result = textManager.GetActiveView2(1, null, (uint)_VIEWFRAMETYPE.vftCodeWindow, out view);
            IVsHiddenTextSession hiddenSession = null;
            IVsTextLines lines = null;
            view.GetBuffer(out lines);
            IVsEnumHiddenRegions[] hiddenRegions = null;
            int hRetVal = hiddenTextManager.GetHiddenTextSession(
                             lines,
                             out hiddenSession);
            if (hRetVal != 0)
            {
                hRetVal = hiddenTextManager.CreateHiddenTextSession(
                             0,
                             lines,
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

                    tags.Add(new TagSpan<DocumentationTag>(snapshotSpan, new DocumentationTag(fragment.Selection.Text, snapshot.CreateTrackingSpan(startPos, length, SpanTrackingMode.EdgeInclusive), _buffer)));
                }
            }

            return tags;
        }
    }
}

﻿//===============================================================================================================
// System  : Sandcastle Help File Builder MSBuild Tasks
// File    : OpenXmlFileGenerator.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 07/08/2025
// Note    : Copyright 2014-2025, Eric Woodruff, All rights reserved
//
// This file contains the class used to finish up creation of the Open XML file parts and compress the
// help content into an Open XML document (a ZIP file with a .docx extension).
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 02/16/2014  EFW  Created the code
// 05/03/2022  EFW  Moved the code into the build engine and removed the task
//===============================================================================================================

// Ignore Spelling: Za num ilvl lvl ul li tbl tc br nolink selflink typeparameter img src cx cy xfrm prst
// Ignore Spelling: rect uri pic dxa dstrike kern sz bdr shd rtl lang

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Sandcastle.Core;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;

namespace SandcastleBuilder.MSBuild.BuildEngine
{
    /// <summary>
    /// This class is used to finish up creation of the Open XML file parts and compress the help content into an
    /// Open XML document (a ZIP file with a .docx extension).
    /// </summary>
    public class OpenXmlFileGenerator
    {
        #region Private data members
        //=====================================================================

        // NOTE: These namespaces must appear in the topic template with the given XML namespace identifiers
        private static readonly XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace a14 = "http://schemas.microsoft.com/office/drawing/2010/main";
        private static readonly XNamespace pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";

        // This is used in the content types file as its default namespace
        private static readonly XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";

        // This is used in the relationships part file as its default namespace
        private static readonly XNamespace pr = "http://schemas.openxmlformats.org/package/2006/relationships";

        // This defines a set of parent elements that can contain text that does not need to be wrapped in a run
        private static readonly HashSet<string> ignoredParents = [ "a", "align", "posOffset", "span", "t" ];

        private static readonly Regex reBadChars = new("[^_0-9A-Za-z]");
        private static readonly Regex reLineBreaks = new("\r\n|\r|\n");

        private Dictionary<string, string> externalHyperlinks, images;
        private Dictionary<string, SizeF> imageSizes;
        private List<NumberingStyle> numberingStyles;
        private int bookmarkId, imageId;

        private IProgress<int> progressProvider;
        private HashSet<string> archiveFiles;

        private long fileCount, uncompressedSize;
        private string archiveName;

        private readonly BuildProcess buildProcess;
        private readonly string workingFolder;

        private static readonly string[] reorderRunProps = [ "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps",
                "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint", "noProof",
                "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs",
                "highlight", "u", "effect", "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang",
                "eastAsianLayout", "specVanish", "oMath" ];
        private static readonly char[] hrefSeparators = [':', '/', '\\'];

        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="buildProcess">The build process to use</param>
        public OpenXmlFileGenerator(BuildProcess buildProcess)
        {
            this.buildProcess = buildProcess ?? throw new ArgumentNullException(nameof(buildProcess));

            workingFolder = Path.Combine(buildProcess.WorkingFolder, "Output", "OpenXml");
        }
        #endregion

        #region Execute methods
        //=====================================================================

        /// <summary>
        /// This is used to execute the task and perform the build
        /// </summary>
        /// <returns>True on success or false on failure.</returns>
        public bool Execute()
        {
            XDocument topic;
            bool isFirstTopic = true;
            string key, lastKey = null, documentPart = Path.Combine(workingFolder, "word", "document.xml");
            int topicCount = 0;

            externalHyperlinks = [];
            images = [];
            imageSizes = [];
            numberingStyles = [];
            archiveFiles = new(StringComparer.OrdinalIgnoreCase);

            // Get the key for the last topic.  We won't add a section break after it.
            // Don't use a simplified using here as we're reading the file again below.
            using(var tocReader = XmlReader.Create(Path.Combine(workingFolder, "..", "..", "toc.xml"),
              new XmlReaderSettings { CloseInput = true }))
            {
                while(tocReader.Read())
                {
                    if(tocReader.NodeType == XmlNodeType.Element && tocReader.Name == "topic")
                    {
                        key = tocReader.GetAttribute("file");

                        if(!String.IsNullOrWhiteSpace(key))
                            lastKey = key;
                    }
                }
            }

            // Bookmark numbering starts at zero, image numbering starts at one
            imageId = 1;

            // Don't use a simplified using here.  We need to ensure the file is closed before moving it below.
            using(var writer = XmlWriter.Create(documentPart + ".tmp", new XmlWriterSettings { CloseOutput = true }))
            {
                // Copy content from the template part.  When the body element is reached, insert the topics.
                using var reader = XmlReader.Create(documentPart, new XmlReaderSettings { CloseInput = true });
                while(!reader.EOF)
                {
                    reader.Read();

                    switch(reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                            writer.WriteAttributes(reader, true);

                            if(String.Equals(reader.LocalName, "body", StringComparison.Ordinal))
                            {
                                // Load the TOC file and process the topics in TOC order
                                using var tocReader = XmlReader.Create(Path.Combine(workingFolder, "..", "..",
                                    "toc.xml"), new XmlReaderSettings { CloseInput = true });
                                    
                                while(tocReader.Read())
                                {
                                    if(tocReader.NodeType == XmlNodeType.Element && tocReader.Name == "topic")
                                    {
                                        key = tocReader.GetAttribute("file");

                                        if(!String.IsNullOrWhiteSpace(key))
                                        {
                                            // The topics are easier to update as XDocuments since we
                                            // have to deal with all the namespaces.  Plus we can use
                                            // LINQ to XML to find stuff.
                                            topic = XDocument.Load(Path.Combine(workingFolder, "Topics",
                                                key + ".xml"));

                                            this.ApplyChanges(topic, key);

                                            // This is not optimal but it's the only way to get
                                            // indentation to work properly if enabled.
                                            using StringReader sr = new(topic.ToString(
                                                buildProcess.CurrentProject.IndentHtml ? SaveOptions.None :
                                                SaveOptions.DisableFormatting));

                                            // Add the processed body content to the combined document.
                                            using var topicReader = XmlReader.Create(sr);

                                            while(topicReader.Read())
                                            {
                                                if(topicReader.NodeType == XmlNodeType.Element &&
                                                    topicReader.LocalName == "body")
                                                {
                                                    WriteTopic(writer, topicReader);
                                                }
                                            }

                                            // Output a next page section break between topics
                                            if(isFirstTopic)
                                            {
                                                // The first one has to include the header and footer references
                                                writer.WriteRaw(
                                                    "<w:p>" +
                                                        "<w:pPr>" +
                                                            "<w:sectPr>" +
                                                                "<w:headerReference w:type=\"default\" r:id=\"rId8\"/>" +
                                                                "<w:footerReference w:type=\"default\" r:id=\"rId9\"/>" +
                                                                "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
                                                                "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" " +
                                                                    "w:left=\"1440\" w:header=\"720\" w:footer=\"720\" " +
                                                                    "w:gutter=\"0\"/>" +
                                                                "<w:cols w:space=\"720\"/>" +
                                                                "<w:docGrid w:linePitch=\"360\"/>" +
                                                            "</w:sectPr>" +
                                                        "</w:pPr>" +
                                                    "</w:p>");

                                                isFirstTopic = false;
                                            }
                                            else
                                            {
                                                // Section breaks are suppressed for member list topics
                                                // which are not rendered.  They still contain a bookmark
                                                // to the topic so that links to overload lists still work
                                                // and effectively go to the first overload in the set.
                                                if(key != lastKey && topic.Root.Attribute("suppressPageBreak") == null)
                                                {
                                                    writer.WriteRaw(
                                                        "<w:p>" +
                                                            "<w:pPr>" +
                                                                "<w:sectPr>" +
                                                                    "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
                                                                    "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" " +
                                                                        "w:left=\"1440\" w:header=\"720\" w:footer=\"720\" " +
                                                                        "w:gutter=\"0\"/>" +
                                                                    "<w:cols w:space=\"720\"/>" +
                                                                    "<w:docGrid w:linePitch=\"360\"/>" +
                                                                "</w:sectPr>" +
                                                            "</w:pPr>" +
                                                        "</w:p>");
                                                }
                                            }
                                        }

                                        topicCount++;

                                        if((topicCount % 500) == 0)
                                            buildProcess.ReportProgress("{0} topics merged", topicCount);
                                    }
                                }
                            }
                            else
                            {
                                if(reader.IsEmptyElement)
                                    writer.WriteEndElement();
                            }
                            break;

                        case XmlNodeType.Text:
                            writer.WriteString(reader.Value);
                            break;

                        case XmlNodeType.Whitespace:
                        case XmlNodeType.SignificantWhitespace:
                            writer.WriteWhitespace(reader.Value);
                            break;

                        case XmlNodeType.CDATA:
                            writer.WriteCData(reader.Value);
                            break;

                        case XmlNodeType.EntityReference:
                            writer.WriteEntityRef(reader.Name);
                            break;

                        case XmlNodeType.XmlDeclaration:
                        case XmlNodeType.ProcessingInstruction:
                            writer.WriteProcessingInstruction(reader.Name, reader.Value);
                            break;

                        case XmlNodeType.DocumentType:
                            writer.WriteDocType(reader.Name, reader.GetAttribute("PUBLIC"),
                                reader.GetAttribute("SYSTEM"), reader.Value);
                            break;

                        case XmlNodeType.Comment:
                            writer.WriteComment(reader.Value);
                            break;

                        case XmlNodeType.EndElement:
                            writer.WriteFullEndElement();
                            break;
                    }
                }

                writer.WriteEndDocument();
            }

            buildProcess.ReportProgress("Finished merging {0} topics", topicCount);

            // Replace the template part with the combined set of topics
            File.Delete(documentPart);
            File.Move(documentPart + ".tmp", documentPart);

            // Save relationships and numbering styles to the part files
            this.SaveRelationships();
            this.SaveNumberingStyles();

            this.GenerateFileList();

            // Create the document by compressing the document parts into a single file
            this.CompressHelpContent();

            // This format works best with 1000 or less topics.  Past that point, the files tend to get rather
            // unmanageable and working with them becomes increasingly more difficult due to their sheer size
            // and actual page count.
            if(topicCount > 1000)
            {
                buildProcess.ReportWarning("BOF0001", "There are {0} topics in this project.  The resulting " +
                    "document may be hard to work with.  See the Open XML Document Help File Format topic in " +
                    "the help file for details.", topicCount);
            }

            return true;
        }

        /// <summary>
        /// This is used to write out the body content of a topic to the main document part
        /// </summary>
        /// <param name="writer">The writer for the main document part</param>
        /// <param name="reader">The reader for the topic body content</param>
        /// <remarks>Using a reader prevents unnecessary namespaces from appearing on the body content elements
        /// which happens if we convert the XElement content to a string and write it out as raw content.</remarks>
        private static void WriteTopic(XmlWriter writer, XmlReader reader)
        {
            while(!reader.EOF)
            {
                reader.Read();

                switch(reader.NodeType)
                {
                    case XmlNodeType.Element:
                        writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                        writer.WriteAttributes(reader, true);

                        if(reader.IsEmptyElement)
                            writer.WriteEndElement();
                        break;

                    case XmlNodeType.Text:
                        writer.WriteString(reader.Value);
                        break;

                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        writer.WriteWhitespace(reader.Value);
                        break;

                    case XmlNodeType.CDATA:
                        writer.WriteCData(reader.Value);
                        break;

                    case XmlNodeType.EntityReference:
                        writer.WriteEntityRef(reader.Name);
                        break;

                    case XmlNodeType.Comment:
                        writer.WriteComment(reader.Value);
                        break;

                    case XmlNodeType.EndElement:
                        // Stop when the end of the body is reached
                        if(reader.LocalName == "body")
                            return;

                        writer.WriteFullEndElement();
                        break;
                }
            }
        }
        #endregion

        #region Open XML clean up tasks
        //=====================================================================

        /// <summary>
        /// Apply the clean up tasks to the given document
        /// </summary>
        /// <param name="document">The document to clean up</param>
        /// <param name="key">The topic key</param>
        private void ApplyChanges(XDocument document, string key)
        {
            // Open XML doesn't support word break elements for text wrapping
            RemoveWordBreakElements(document);

            // Remove invalid spans that we can't use for formatting
            RemoveInvalidSpans(document);

            // Wrap stray text nodes in run/text elements and add containing paragraphs first since the
            // subsequent updates rely on well-formed paragraphs.
            WrapStrayElementNodes(document);
            MakeSpansInTextSiblings(document);
            AddContainingParagraphs(document);

            AddBlankCellParagraphs(document);
            ConvertStyleSpans(document);
            ConvertHtmlLineBreaks(document);

            this.ReformatBookmarkNames(document, key);
            this.ConvertHtmlAnchors(document, key);
            this.ConvertHtmlImages(document);

            // Handle list conversion last
            this.ConvertHtmlLists(document);

            // Finally, add line breaks where necessary to preserve text formatting in things like code blocks
            InsertLineBreaks(document);

            // Make one final pass to wrap remaining stray text nodes that showed up after handling everything.
            // This can happen for stuff in span elements which are skipped by this method.
            WrapStrayElementNodes(document);

            // Ensure tables are at the proper level and have grid columns
            CleanupTables(document);
        }

        /// <summary>
        /// Save the external link and image relationship information
        /// </summary>
        private void SaveRelationships()
        {
            string relationshipsFilename = Path.Combine(workingFolder, "word", "_rels", "document.xml.rels");

            // If the relationships file does not exist, see if it's there under the alternate name used to
            // support the NuGet deployment.  Certain filenames reserved as part of the Open Packaging
            // Conventions cause problems with NuGet packages.  This works around the issue.
            if(!File.Exists(relationshipsFilename))
            {
                string altName = Path.Combine(workingFolder, "word", "_rels", "document.xml_rels");

                if(File.Exists(altName))
                    File.Move(altName, relationshipsFilename);
            }

            XDocument relationshipsFile = XDocument.Load(relationshipsFilename);
            XElement root = relationshipsFile.Root;

            foreach(var kv in externalHyperlinks)
            {
                root.Add(new XElement(pr + "Relationship",
                    new XAttribute("Id", kv.Key),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                    new XAttribute("Target", kv.Value),
                    new XAttribute("TargetMode", "External")));
            }

            foreach(var kv in images)
            {
                root.Add(new XElement(pr + "Relationship",
                    new XAttribute("Id", kv.Key),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                    new XAttribute("Target", kv.Value)));
            }

            relationshipsFile.Save(relationshipsFilename);

            buildProcess.ReportProgress("{0} external hyperlink relationship(s) defined", externalHyperlinks.Count);
            buildProcess.ReportProgress("{0} image relationship(s) defined", images.Count);
        }

        /// <summary>
        /// Save list numbering styles
        /// </summary>
        /// <remarks>Numbering styles are rather complex.  To keep it as simple as possible, we use one common
        /// abstract definition and use separate numbering definitions with level overrides to apply "ordered"
        /// or "no bullet" styles to the lists that use the same style at the same level.</remarks>
        private void SaveNumberingStyles()
        {
            string numberingFilename = Path.Combine(workingFolder, "word", "numbering.xml");
            XDocument numberingFile = XDocument.Load(numberingFilename);
            XElement root = numberingFile.Root;

            foreach(var style in numberingStyles)
            {
                var num = new XElement(w + "num",
                    new XAttribute(w + "numId", style.Id.ToString(CultureInfo.InvariantCulture)),
                    new XElement(w + "abstractNumId", new XAttribute(w + "val", "0")));

                var lvlOverride = new XElement(w + "lvlOverride", new XAttribute(w + "ilvl",
                    style.Level.ToString(CultureInfo.InvariantCulture)));

                if(style.Style == "ordered")
                {
                    // A starting number override is required in order to reset numbering.  It must match the
                    // w:start element value added below.
                    lvlOverride.Add(new XElement(w + "startOverride", new XAttribute(w + "val",
                        style.Start.ToString(CultureInfo.InvariantCulture))));

                    lvlOverride.Add(new XElement(w + "lvl", new XAttribute(w + "ilvl",
                        style.Level.ToString(CultureInfo.InvariantCulture)),
                        new XElement(w + "start", new XAttribute(w + "val",
                            style.Start.ToString(CultureInfo.InvariantCulture))),
                        new XElement(w + "numFmt", new XAttribute(w + "val", "decimal")),
                        new XElement(w + "lvlText", new XAttribute(w + "val",
                            "%" + (style.Level + 1).ToString(CultureInfo.InvariantCulture) + ".")),
                        new XElement(w + "lvlJc", new XAttribute(w + "val", "left")),
                        new XElement(w + "pPr",
                            new XElement(w + "ind",
                                new XAttribute(w + "left",
                                    (720 * (style.Level + 1)).ToString(CultureInfo.InvariantCulture)),
                                new XAttribute(w + "hanging", "360")))));
                }
                else
                {
                    lvlOverride.Add(new XElement(w + "lvl", new XAttribute(w + "ilvl",
                        style.Level.ToString(CultureInfo.InvariantCulture)),
                        new XElement(w + "start", new XAttribute(w + "val", "1")),
                        new XElement(w + "numFmt", new XAttribute(w + "val", "bullet")),
                        new XElement(w + "lvlText", new XAttribute(w + "val", " ")),
                        new XElement(w + "lvlJc", new XAttribute(w + "val", "left")),
                        new XElement(w + "pPr",
                            new XElement(w + "ind",
                                new XAttribute(w + "left",
                                    (720 * (style.Level + 1)).ToString(CultureInfo.InvariantCulture)),
                                new XAttribute(w + "hanging", "360")))));
                }

                num.Add(lvlOverride);
                root.Add(num);
            }

            numberingFile.Save(numberingFilename);

            buildProcess.ReportProgress("{0} list numbering style override(s) defined", numberingStyles.Count);
        }
        #endregion

        #region General element fix up methods
        //=====================================================================

        /// <summary>
        /// Remove word break elements
        /// </summary>
        /// <param name="document">The document in which to remove the word breaks</param>
        private static void RemoveWordBreakElements(XDocument document)
        {
            foreach(var wbr in document.Descendants("wbr").ToArray())
            {
                wbr.Remove();
            }
        }

        /// <summary>
        /// Add containing paragraphs for run elements that don't have one
        /// </summary>
        /// <param name="document">The document in which to add containing paragraphs</param>
        /// <remarks>MAML and XML comments may not be well formed such that all text runs end up inside a
        /// paragraph.  While HTML is very forgiving in that respect, Open XML is not and it will cause the
        /// document to appear to be corrupted.  This attempts to fix up such ill-formed content.  It is not
        /// perfect so there may still be issues.  Additional fix ups can be added as they are found but this is
        /// no substitute for using well-formed content in the first place.</remarks>
        private static void AddContainingParagraphs(XDocument document)
        {
            CheckForContainingParagraph(document.Descendants(w + "r"));
            CheckForContainingParagraph(document.Descendants(w + "hyperlink"));
            CheckForContainingParagraph(document.Descendants("span"));
            CheckForContainingParagraph(document.Descendants("a"));
        }

        /// <summary>
        /// Check for a containing paragraph on each of the given elements
        /// </summary>
        /// <param name="elements">An enumerable list of elements to check</param>
        private static void CheckForContainingParagraph(IEnumerable<XElement> elements)
        {
            foreach(var element in elements.ToList())
            {
                string localName = element.Parent.Name.LocalName;

                if(localName != "p" && localName != "a" && localName != "span" && localName != "hyperlink" &&
                  localName != "t")
                {
                    var para = new XElement(w + "p");
                    var currentElement = element;

                    currentElement.AddBeforeSelf(para);

                    // Pull all following paragraph elements into the paragraph too
                    do
                    {
                        var nn = currentElement.NextNode;

                        currentElement.Remove();
                        para.Add(currentElement);

                        if(nn is not XElement nextNode)
                            break;

                        currentElement = nextNode;
                        localName = currentElement.Name.LocalName;

                    } while(localName != "p" && localName != "ul" && localName != "li" &&
                        localName != "bookmarkStart" && localName != "tbl");
                }
            }
        }

        /// <summary>
        /// Add empty paragraphs to empty cells
        /// </summary>
        /// <param name="document">The document in which to add paragraphs to empty cells</param>
        /// <remarks>Table cells must contain a paragraph element</remarks>
        private static void AddBlankCellParagraphs(XDocument document)
        {
            foreach(var cell in document.Descendants(w + "tc").Where(c => !c.HasElements && c.IsEmpty))
                cell.Add(new XElement(w + "p"));
        }

        /// <summary>
        /// Convert HTML line breaks to Open XML line breaks
        /// </summary>
        /// <param name="document">The document in which to apply the fix-ups</param>
        /// <remarks>HTML line breaks can appear in content items and the transformations where it may not be
        /// convenient or possible to insert the containing run element.  This fixes them up so that they are
        /// correct.</remarks>
        private static void ConvertHtmlLineBreaks(XDocument document)
        {
            foreach(var lineBreak in document.Descendants("br").ToList())
                lineBreak.ReplaceWith(new XElement(w + "r", new XElement(w + "br")));
        }

        /// <summary>
        /// Wrap stray text nodes in text elements and, when necessary, run elements
        /// </summary>
        /// <param name="document">The document in which to wrap stray text nodes</param>
        /// <remarks>Stray text nodes can occur when resolving shared content items.  We need to ensure that
        /// all text nodes are within a text element within a run to ensure that the document does not appear to
        /// be corrupted.</remarks>
        private static void WrapStrayElementNodes(XDocument document)
        {
            XElement wrap;

            foreach(var text in document.DescendantNodes().OfType<XText>().Where(
              t => !ignoredParents.Contains(t.Parent.Name.LocalName)).ToList())
            {
#if DEBUG
                if(text.Parent.Name.LocalName != "body" && text.Parent.Name.LocalName != "p" && text.Parent.Name.LocalName != "tc")
                    System.Diagnostics.Debug.WriteLine("Stray text parent: " + text.Parent.Name.LocalName);
#endif
                if(text.Parent.Name.LocalName == "r")
                    wrap = new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text.Value);
                else
                    wrap = new XElement(w + "r", new XElement(w + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), text.Value));

                text.ReplaceWith(wrap);
            }
        }

        /// <summary>
        /// Move spans inside of text elements outside the parent text element as a sibling
        /// </summary>
        /// <param name="document">The document to check for spans inside of text elements</param>
        /// <remarks>This can occur when unresolved links occur within a paragraph</remarks>
        private static void MakeSpansInTextSiblings(XDocument document)
        {
            foreach(var span in document.Descendants("span").Where(s => s.Parent.Name.LocalName == "t"))
            {
                XElement parent = span.Parent.Parent;
                XNode next = span.NextNode;
                span.Remove();
                parent.AddAfterSelf(span);

                // Move all following nodes after the span as well
                XNode nn = span;

                while(next != null)
                {
                    var n = next;
                    next = next.NextNode;
                    n.Remove();

                    if(n is XText t)
                    {
                        nn.AddAfterSelf(new XElement(w + "r", new XElement(w + "t", t)));
                        nn = t.Parent.Parent;
                    }
                    else
                    {
                        nn.AddAfterSelf(n);
                        nn = n;
                    }
                }
            }
        }

        /// <summary>
        /// This cleans up table elements so that they include the correct w:tblGrid
        /// element and that they are not wrapped within a w:p element.
        /// </summary>
        /// <param name="document">The document in which to clean up any tables</param>
        private static void CleanupTables(XDocument document)
        {
            foreach(var table in document.Descendants(w + "tbl").ToArray())
            {
                XElement tblGrid = table.Element(w + "tblGrid");

                if(tblGrid == null)
                {
                    // If this triggers, we forgot to add cells to a row or rows to a table
                    if(!table.Elements(w + "tr").Any() || table.Elements(w + "tr").Any(r => !r.Elements(w + "tc").Any()))
                        System.Diagnostics.Debugger.Break();

                    // Get the count of table cells and create that many <w:gridCol /> elements
                    // in our <w:tblGrid> element.
                    int cellCount = table.Elements(w + "tr").Max(tr => tr.Elements(w + "tc").Count());
                    tblGrid = new XElement(w + "tblGrid",
                        Enumerable.Range(0, cellCount).Select(_ => new XElement(w + "gridCol")));

                    // Then put the <w:tblGrid> element just before the first <w:tr>
                    table.Element(w + "tr").AddBeforeSelf(tblGrid);
                }

                // Check if this table needs to be "unwrapped" from a parent <w:p>
                if(table.Parent.Name == w + "p")
                {
                    var p = table.Parent;

                    // The table may be preceded by a paragraph containing a title and followed by other content
                    // such as another table. As such, move all content to the parent.
                    foreach(var child in p.Elements().ToArray())
                    {
                        child.Remove();
                        p.AddBeforeSelf(child);
                    }

                    p.Remove();
                }
            }
        }

        /// <summary>
        /// Insert line break elements where needed to preserve text formatting
        /// </summary>
        /// <param name="document">The document in which to insert line breaks</param>
        private static void InsertLineBreaks(XDocument document)
        {
            XElement element, lastElement = null;
            string[] parts;
            string part;
            bool skipBreak;

            foreach(var splitText in document.DescendantNodes().OfType<XElement>().Where(
              t => t.Name.LocalName == "t" && (string)t.Attribute(XNamespace.Xml + "space") == "preserve" &&
              reLineBreaks.IsMatch(t.Value)).ToList())
            {
                // For single line breaks, use an empty string so that we don't gain an extra line break
                if(splitText.Value == "\r\n" || splitText.Value == "\r" || splitText.Value == "\n")
                    splitText.Value = String.Empty;

                parts = reLineBreaks.Split(splitText.Value);
                skipBreak = false;

                for(int idx = 0; idx < parts.Length; idx++)
                {
                    part = parts[idx];

                    if(idx == 0)
                    {
                        // The source text element gets the first part or is replaced with a line break if empty
                        if(part.Length != 0)
                        {
                            splitText.Value = part;
                            lastElement = splitText;
                        }
                        else
                        {
                            lastElement = new XElement(w + "br");
                            splitText.ReplaceWith(lastElement);
                            skipBreak = true;   // Prevent an extra line break from being added after this one
                        }
                    }
                    else
                    {
                        if(!skipBreak)
                        {
                            element = new XElement(w + "br");
                            lastElement.AddAfterSelf(element);
                            lastElement = element;
                        }
                        else
                            skipBreak = false;

                        if(part.Length != 0)
                        {
                            element = new XElement(w + "t",
                                new XAttribute(XNamespace.Xml + "space", "preserve"), part);
                            lastElement.AddAfterSelf(element);
                            lastElement = element;
                        }
                    }
                }
            }
        }
        #endregion

        #region Style span fix conversion methods
        //=====================================================================

        /// <summary>
        /// Remove spans without a class attribute that cannot be used for formatting
        /// </summary>
        /// <param name="document">The document in which to remove invalid spans</param>
        /// <remarks>The transformation could do this but it wouldn't necessarily cover third party build
        /// components which could introduce invalid spans so we'll take care of them all here.</remarks>
        private static void RemoveInvalidSpans(XDocument document)
        {
            foreach(var span in document.Descendants("span").Where(s => (string)s.Attribute("class") == null).ToList())
            {
                foreach(var child in span.Nodes().ToList())
                {
                    child.Remove();
                    span.AddBeforeSelf(child);
                }

                span.Remove();
            }
        }

        /// <summary>
        /// Convert style spans to Open XML run formatting
        /// </summary>
        /// <param name="document">The document in which to convert the style spans</param>
        /// <remarks>Nested spans result in run formatting that is accumulated in each run in the nested set of
        /// spans.</remarks>
        private static void ConvertStyleSpans(XDocument document)
        {
            // Remove language-specific text.  Do this before other spans as these may not be in paragraphs.
            foreach(var span in document.Descendants("span").Where(
              s => s.Attribute("class")?.Value == "languageSpecificText").ToList())
            {
                ApplySpanFormatting(span, new XElement(new XElement(w + "rPr")));
            }

            // Get all paragraphs with a span as a child and condense the span formatting
            foreach(var span in document.Descendants(w + "p").Elements("span").ToList())
                ApplySpanFormatting(span, new XElement(new XElement(w + "rPr")));

            // Remove language specific text formatting from within HTML anchors too
            foreach(var span in document.Descendants("a").Elements("span").ToList())
                ApplySpanFormatting(span, new XElement(new XElement(w + "rPr")));
        }

        /// <summary>
        /// Apply the formatting from a span including all nested spans to each run contained within it
        /// </summary>
        /// <param name="span">The root span from which to start applying formatting</param>
        /// <param name="runProps">The run properties in which to accumulate formatting</param>
        private static void ApplySpanFormatting(XElement span, XElement runProps)
        {
            string spanClass = (string)span.Attribute("class");

            switch(spanClass)
            {
                case null:
                    // Missing class name, ignore it.  Shouldn't see any at this point, but just in case.
                    break;

                case "languageSpecificText":
                    // Replace language-specific text with the neutral text sub-entry.  If not found, remove it.
                    var genericText = span.Elements("span").FirstOrDefault(s => (string)s.Attribute("class") == "nu");

                    if(genericText != null)
                    {
                        if(span.Parent.Name.LocalName != "t")
                            span.ReplaceWith(new XElement(w + "r", new XElement(w + "t", genericText.Value)));
                        else
                            span.ReplaceWith(genericText.Value);
                    }
                    else
                        span.Remove();
                    return;

                case "Bold":
                case "noLink":
                case "NoLink":
                    if(!runProps.Elements(w + "b").Any())
                        runProps.Add(new XElement(w + "b"));
                    break;

                case "Emphasis":
                    if(!runProps.Elements(w + "i").Any())
                        runProps.Add(new XElement(w + "i"));
                    break;

                case "Underline":
                    if(!runProps.Elements(w + "u").Any())
                        runProps.Add(new XElement(w + "u", new XAttribute(w + "val", "single")));
                    break;

                case "Subscript":       // If vertAlign exists, replace it as we can't merge it
                    var subscript = runProps.Elements(w + "vertAlign").FirstOrDefault();

                    if(subscript != null)
                        subscript.Attribute(w + "val").Value = "subscript";
                    else
                        runProps.Add(new XElement(w + "vertAlign", new XAttribute(w + "val", "subscript")));
                    break;

                case "Superscript":     // If vertAlign exists, replace it as we can't merge it
                    var superscript = runProps.Elements(w + "vertAlign").FirstOrDefault();

                    if(superscript != null)
                        superscript.Attribute(w + "val").Value = "superscript";
                    else
                        runProps.Add(new XElement(w + "vertAlign", new XAttribute(w + "val", "superscript")));
                    break;

                default:                // Named style
                    // Correct the casing on code and syntax section style names
                    switch(spanClass)
                    {
                        case "comment":
                        case "identifier":
                        case "keyword":
                        case "literal":
                        case "parameter":
                            spanClass = spanClass[0].ToString().ToUpperInvariant() + spanClass.Substring(1);
                            break;

                        case "selflink":
                            spanClass = "SelfLink";
                            break;

                        case "typeparameter":
                            spanClass = "TypeParameter";
                            break;

                        default:
                            break;
                    }

                    // If one exists, replace it since we can't merge them
                    var namedStyle = runProps.Elements(w + "rStyle").FirstOrDefault();

                    if(namedStyle != null)
                        namedStyle.Attribute(w + "val").Value = spanClass;
                    else
                        runProps.AddFirst(new XElement(w + "rStyle", new XAttribute(w + "val", spanClass)));
                    break;
            }

            // If the span does not have children but is not empty, it has inner text that needs to be wrapped
            // in a run.
            if(!span.HasElements && !span.IsEmpty)
            {
                var content = new XElement(w + "r", new XElement(w + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), span.Value));
                span.Value = String.Empty;
                span.Add(content);
            }

            // Ensure that the order of rPr children is correct
            runProps = ReorderChildren(runProps, reorderRunProps);

            // Add the run properties to each child run
            if(runProps.HasElements)
            {
                foreach(var run in span.Elements(w + "r"))
                    run.AddFirst(new XElement(runProps));
            }

            // Handle nested spans.  These will accumulate the formatting of the parent spans.
            foreach(var nestedSpan in span.Elements("span").ToList())
                ApplySpanFormatting(nestedSpan, new XElement(runProps));

            // Now move the content up to the parent
            foreach(var child in span.Nodes().ToList())
            {
                child.Remove();
                span.AddBeforeSelf(child);
            }

            // And finally, remove the span
            span.Remove();
        }

        /// <summary>
        /// Reorders an elements' children by a specific ordering.
        /// </summary>
        /// <param name="element">Element whose children should be in a specific order</param>
        /// <param name="orderings">The specific order of child element, by local name</param>
        /// <returns><paramref name="element" /> with its children reordered.</returns>
        private static XElement ReorderChildren(XElement element, IList<string> orderings)
        {
            var orderedChildren = element.Elements().OrderBy(e => orderings.IndexOf(e.Name.LocalName)).ToArray();

            element.ReplaceNodes(orderedChildren);

            return element;
        }
        #endregion

        #region Bookmark fix up method
        //=====================================================================

        /// <summary>
        /// Reformat the bookmark names to prefix them with the topic key so that they are unique and do not
        /// contain any invalid characters.
        /// </summary>
        /// <param name="document">The document in which to reformat the bookmark names</param>
        /// <param name="key">The document key</param>
        private void ReformatBookmarkNames(XDocument document, string key)
        {
            XElement bookmarkEnd;
            string name;

            // Bookmark names are limited to 40 characters.  As such, use a hash of the key to keep it short.
            // Start each one with an underscore so that it is valid and hidden.
            string keyHash = "_" + key.ToUpperInvariant().GetHashCodeDeterministic().ToString("X", CultureInfo.InvariantCulture);

            foreach(var bookmarkStart in document.Descendants(w + "bookmarkStart"))
            {
                // Don't prefix the topic bookmark with "_Topic"
                name = bookmarkStart.Attribute(w + "name").Value;

                if(name == "_Topic")
                    bookmarkStart.Attribute(w + "name").Value = reBadChars.Replace(keyHash + name, "_");
                else
                    bookmarkStart.Attribute(w + "name").Value = reBadChars.Replace(keyHash + "_Topic" + name, "_");

                // Bookmark IDs must be unique across all topics.  Links use the name to reference them but the
                // bookmarkEnd elements use the ID so we need to update it there too.  In this case, the end
                // element should immediately follow the start element since we never wrap content in a bookmark.
                bookmarkStart.Attribute(w + "id").Value = bookmarkId.ToString(CultureInfo.InvariantCulture);

                bookmarkEnd = bookmarkStart.NextNode as XElement;

                if(bookmarkEnd != null && bookmarkEnd.Name.LocalName == "bookmarkEnd")
                    bookmarkEnd.Attribute(w + "id").Value = bookmarkId.ToString(CultureInfo.InvariantCulture);

                bookmarkId++;
            }

            // Update hyperlink anchors that don't contain the key (those added by the transformations)
            keyHash += "_Topic";

            foreach(var link in document.Descendants(w + "hyperlink").Where(
              l => l.Attribute(w + "anchor") != null && !l.Attribute(w + "anchor").Value.Contains(key)))
            {
                link.Attribute(w + "anchor").Value = reBadChars.Replace(keyHash +
                    link.Attribute(w + "anchor").Value, "_");
            }
        }
        #endregion

        #region HTML anchor conversion method
        //=====================================================================

        /// <summary>
        /// Convert HTML anchor elements to Open XML hyperlink elements
        /// </summary>
        /// <param name="document">The document in which to convert the HTML anchor elements</param>
        /// <param name="key">The document key</param>
        private void ConvertHtmlAnchors(XDocument document, string key)
        {
            XElement hyperlink;
            string href, title, innerText, id;
            string[] parts;

            foreach(var anchor in document.Descendants("a").ToList())
            {
                href = (string)anchor.Attribute("href");

                if(String.IsNullOrWhiteSpace(href))
                    anchor.Remove();
                else
                {
                    title = (string)anchor.Attribute("title");

                    if(!anchor.HasElements)
                    {
                        // Plain text
                        innerText = anchor.Value;

                        if(String.IsNullOrWhiteSpace(innerText))
                            innerText = href;

                        hyperlink = new XElement(w + "hyperlink",
                            !String.IsNullOrWhiteSpace(title) ? new XAttribute(w + "tooltip", title) : null,
                            new XAttribute(w + "history", "1"),
                            new XElement(w + "r",
                                new XElement(w + "rPr",
                                    new XElement(w + "rStyle", new XAttribute(w + "val", "Hyperlink"))),
                                new XElement(w + "t", innerText)));
                    }
                    else
                    {
                        // Elements
                        hyperlink = new XElement(w + "hyperlink",
                            !String.IsNullOrWhiteSpace(title) ? new XAttribute(w + "tooltip", title) : null,
                            new XAttribute(w + "history", "1"));

                        foreach(var content in anchor.Nodes())
                        {
                            if(content is not XText text || text.Parent != anchor)
                                hyperlink.Add(content);
                            else
                                hyperlink.Add(new XElement(w + "r", new XElement(w + "t", text.Value)));
                        }

                        // Apply hyperlink formatting to each run unless it has another style
                        foreach(var run in hyperlink.Descendants(w + "r"))
                        {
                            var runProps = run.Element(w + "rPr");

                            if(runProps == null)
                            {
                                run.AddFirst(new XElement(w + "rPr",
                                    new XElement(w + "rStyle", new XAttribute(w + "val", "Hyperlink"))));
                            }
                            else
                            {
                                var runStyle = runProps.Element(w + "rStyle");

                                if(runStyle == null)
                                    runProps.AddFirst(new XElement(w + "rStyle", new XAttribute(w + "val", "Hyperlink")));
                            }
                        }
                    }

                    if(href.IndexOfAny(hrefSeparators) != -1)
                    {
                        // External link.  Add a relationship ID and track the id/URL pair.  These are stored in
                        // the relationships file.
                        id = String.Format(CultureInfo.InvariantCulture, "elId{0:X}",
                            href.ToUpperInvariant().GetHashCodeDeterministic());

                        if(!externalHyperlinks.ContainsKey(id))
                            externalHyperlinks.Add(id, href);

                        hyperlink.Add(new XAttribute(r + "id", id));
                    }
                    else
                    {
                        // Local link.  Fix up the format to match the bookmark ID and add an anchor attribute.
                        parts = href.Split('#');

                        // Bookmark names are limited to 40 characters.  As such, use a hash of the key to keep
                        // it short.
                        if(parts.Length == 1)
                        {
                            href = Path.GetFileNameWithoutExtension(href).ToUpperInvariant().GetHashCodeDeterministic().ToString(
                                "X", CultureInfo.InvariantCulture) + "_Topic";
                        }
                        else
                        {
                            // In-page link?
                            if(parts[0].Length == 0)
                            {
                                href = key.ToUpperInvariant().GetHashCodeDeterministic().ToString("X",
                                    CultureInfo.InvariantCulture) + "_Topic_" + parts[1];
                            }
                            else
                            {
                                href = Path.GetFileNameWithoutExtension(parts[0]).ToUpperInvariant().GetHashCodeDeterministic().ToString(
                                    "X", CultureInfo.InvariantCulture) + "_Topic_" + parts[1];
                            }
                        }

                        href = reBadChars.Replace("_" + href, "_");

                        hyperlink.Add(new XAttribute(w + "anchor", href));
                    }

                    if(anchor.Parent.Name.LocalName == "r")
                        anchor.Parent.ReplaceWith(hyperlink);
                    else
                    {
                        // If inside a text element in a run, make it a sibling of the containing run
                        if(anchor.Parent.Name.LocalName == "t")
                        {
                            anchor.Parent.Parent.AddAfterSelf(hyperlink);

                            // Move all following nodes after the hyperlink as well
                            XNode nn = hyperlink;

                            while(anchor.NextNode != null)
                            {
                                var n = anchor.NextNode;
                                n.Remove();

                                if(n is XText t)
                                {
                                    nn.AddAfterSelf(new XElement(w + "r", new XElement(w + "t", t)));
                                    nn = t.Parent.Parent;
                                }
                                else
                                {
                                    nn.AddAfterSelf(n);
                                    nn = n;
                                }
                            }

                            anchor.Remove();
                        }
                        else
                            anchor.ReplaceWith(hyperlink);
                    }
                }
            }

            // Converting hyperlinks may move some additional span elements up to the paragraph level that
            // haven't been handled yet so we'll take care of them now.
            foreach(var span in document.Descendants(w + "p").Elements("span").ToList())
                ApplySpanFormatting(span, new XElement(new XElement(w + "rPr")));
        }
        #endregion

        #region HTML Image conversion method
        //=====================================================================

        /// <summary>
        /// Convert HTML image elements to Open XML drawing elements
        /// </summary>
        /// <param name="document">The document in which to convert the image elements</param>
        private void ConvertHtmlImages(XDocument document)
        {
            string alt, src, id;

            foreach(var image in document.Descendants("img").ToList())
            {
                alt = (string)image.Attribute("alt");
                src = (string)image.Attribute("src");

                if(String.IsNullOrWhiteSpace(src))
                {
                    image.Remove();
                    continue;
                }

                // Set the relationship ID and track the id/image pair.  These are stored in the relationships
                // file.
                id = String.Format(CultureInfo.InvariantCulture, "imgId{0:X}", src.ToUpperInvariant().GetHashCodeDeterministic());

                if(!images.ContainsKey(id))
                    images.Add(id, src);

                // The drawing element is rather complex so we'll build the parts separately to make this a bit
                // more readable.  Note that it requires us to set the actual extents of the image ("cx" and "cy"
                // in the "xfrm" and "extent" elements).
                this.DetermineImageSize(src, out long cx, out long cy);

                var xfrm = new XElement(a + "xfrm",
                    new XElement(a + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(a + "ext", new XAttribute("cx", cx.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("cy", cy.ToString(CultureInfo.InvariantCulture))));

                var prstGeom = new XElement(a + "prstGeom", new XAttribute("prst", "rect"),
                    new XElement(a + "avLst"));

                var spPr = new XElement(pic + "spPr", xfrm, prstGeom);

                var extLst = new XElement(a + "extLst",
                    new XElement(a + "ext", new XAttribute("uri", "{28A0092B-C50C-407E-A947-70E740481C1C}"),
                        new XElement(a14 + "useLocalDpi", new XAttribute("val", "0"))));

                // The embed attribute identifies the image part by relationship ID and the ID can be used on
                // multiple drawing instances that use the same image.
                var blip = new XElement(a + "blip", new XAttribute(r + "embed", id), extLst);

                var stretch = new XElement(a + "stretch", new XElement(a + "fillRect"));

                var blipFill = new XElement(pic + "blipFill", blip, stretch);

                // For this element, ID appears to always be zero and does not have to be unique
                var cNvPr = new XElement(pic + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", src));

                var nvPicPr = new XElement(pic + "nvPicPr", cNvPr, new XElement(pic + "cNvPicPr"));

                var picPic = new XElement(pic + "pic", nvPicPr, blipFill, spPr);

                var graphic = new XElement(a + "graphic",
                    new XElement(a + "graphicData",
                        new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                        picPic));

                var cNvGraphicFramePr = new XElement(wp + "cNvGraphicFramePr",
                    new XElement(a + "graphicFrameLocks", new XAttribute("noChangeAspect", "1")));

                // The ID in this one must be unique for each drawing element across all topics
                var docPr = new XElement(wp + "docPr",
                    new XAttribute("id", imageId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("name", src),
                    !String.IsNullOrWhiteSpace(alt) ? new XAttribute("title", alt) : null);

                var extent = new XElement(wp + "extent",
                    new XAttribute("cx", cx.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("cy", cy.ToString(CultureInfo.InvariantCulture)));

                var drawing = new XElement(w + "drawing",
                    new XElement(wp + "inline",
                        new XAttribute("distT", "0"), new XAttribute("distB", "0"),
                        new XAttribute("distL", "0"), new XAttribute("distR", "0"),
                            extent, docPr, cNvGraphicFramePr, graphic));

                // Ensure the <w:drawing> is within a <w:r> and not a bare <w:p>.  If it's within the body,
                // add the containing paragraph too.
                if(image.Parent.Name != w + "r")
                {
                    var p = image.Parent;

                    drawing = new XElement(w + "r", drawing);

                    if(p.Name != w + "p")
                        drawing = new XElement(w + "p", drawing);
                }

                image.ReplaceWith(drawing);
                imageId++;
            }
        }

        /// <summary>
        /// This is used to determine the image size in English Metric Units
        /// </summary>
        /// <param name="imageFilename">The image filename</param>
        /// <param name="cx">On return, this will contain the width of the image in English Metric Units</param>
        /// <param name="cy">On return, this will contain the height of the image in English Metric Units</param>
        private void DetermineImageSize(string imageFilename, out long cx, out long cy)
        {
            // All images should be found relative to the main document part's folder
            imageFilename = Path.Combine(workingFolder, "word", imageFilename);

            if(!imageSizes.TryGetValue(imageFilename, out SizeF size))
            {
                size = new SizeF(1.0f, 1.0f);

                if(!File.Exists(imageFilename))
                    buildProcess.ReportWarning("BOF0002", "Unable to locate image file: {0}", imageFilename);
                else
                {
                    using var image = Image.Load(imageFilename);
                    
                    switch(image.Metadata.ResolutionUnits)
                    {
                        case PixelResolutionUnit.AspectRatio:
                            size.Width = image.Width;
                            size.Height = image.Height;
                            break;

                        case PixelResolutionUnit.PixelsPerInch:
                            size.Width = image.Width / (float)image.Metadata.HorizontalResolution;
                            size.Height = image.Height / (float)image.Metadata.VerticalResolution;
                            break;

                        case PixelResolutionUnit.PixelsPerCentimeter:
                            size.Width = image.Width / ((float)image.Metadata.HorizontalResolution * 2.54f);
                            size.Height = image.Height / ((float)image.Metadata.VerticalResolution * 2.54f);
                            break;

                        case PixelResolutionUnit.PixelsPerMeter:
                            size.Width = image.Width / ((float)image.Metadata.HorizontalResolution * 0.0254f);
                            size.Height = image.Height / ((float)image.Metadata.VerticalResolution * 0.0254f);
                            break;
                    }
                }

                imageSizes.Add(imageFilename, size);
            }

            // Measurements are in English Metric Units (914,400 EMU per inch)
            cx = (long)(size.Width * 914400.0);
            cy = (long)(size.Height * 914400.0);
        }
        #endregion

        #region HTML list conversion methods
        //=====================================================================

        /// <summary>
        /// Convert HTML list elements to Open XML list formatting elements and track each unique list style
        /// </summary>
        /// <param name="document">The document in which to convert the HTML anchor elements</param>
        /// <remarks>Open XML lists are not like HTML lists.  The list items have formatting applied that refers
        /// to a numbering properties element stored in a separate part.  The numbering properties refer to
        /// an abstract numbering definition which can be shared amongst many numbering properties elements which
        /// may override levels within the abstract definition.  Also Open XML only supports up to nine levels
        /// of nested lists.</remarks>
        private void ConvertHtmlLists(XDocument document)
        {
            // Get all lists in the document and apply formatting to each nested list and list item.  Lists may
            // appear anywhere so we'll have to do a recursive search and ignore nested lists that got processed
            // during the recursion (no parent).
            foreach(var list in document.Descendants("ul").ToList())
                if(list.Parent != null)
                    this.ApplyListFormatting(list, 0);
        }

        /// <summary>
        /// This applies formatting to each nested list and list item
        /// </summary>
        /// <param name="list">The list to process</param>
        /// <param name="level">The level of this list</param>
        private void ApplyListFormatting(XElement list, int level)
        {
            NumberingStyle numStyle;
            string style;
            int start = -1, id = 1;

            if(level > 8)
                level = 8;

            // Handle nested lists first ignoring those that got processed in a recursive call (no parent)
            foreach(var nestedList in list.Descendants("ul").ToList())
                if(nestedList.Parent != null)
                    this.ApplyListFormatting(nestedList, level + 1);

            style = (string)list.Attribute("class");

            if(!String.IsNullOrWhiteSpace(style) && style != "bullet")
            {
                if(style == "number")
                    style = "ordered";

                if(style == "ordered" && !Int32.TryParse((string)list.Attribute("start"), out start))
                    start = 1;

                // Reuse an existing bullet style override or add a new one.  Numbered lists always get a new one
                // so that numbering starts at the expected value.
                if(style == "ordered")
                    numStyle = null;
                else
                    numStyle = numberingStyles.FirstOrDefault(s => s.Style == style && s.Level == level &&
                        s.Start == start);

                if(numStyle != null)
                    id = numStyle.Id;
                else
                {
                    // Style ID #1 is always the default style
                    id = numberingStyles.Count + 2;
                    numberingStyles.Add(new NumberingStyle { Id = id, Style = style, Level = level, Start = start });
                }
            }

            foreach(var listItem in list.Elements("li").ToList())
            {
                bool isFirstPara = true;

                foreach(var para in listItem.Elements(w + "p"))
                {
                    var props = para.Element(w + "pPr");

                    if(props == null)
                    {
                        props = new XElement(w + "pPr");
                        para.AddFirst(props);
                    }

                    var pStyle = props.Element(w + "pStyle");

                    // If there is a style and it is ListParagraph, it's probably a nested list element that has
                    // been moved up.  If so, leave it alone.
                    if(pStyle == null || (string)pStyle.Attribute(w + "val") != "ListParagraph")
                    {
                        if(pStyle != null)
                            pStyle.Attribute(w + "val").Value = "ListParagraph";
                        else
                            props.AddFirst(new XElement(w + "pStyle", new XAttribute(w + "val", "ListParagraph")));

                        // Only apply the numbering style to the first paragraph.  Subsequent paragraphs in the
                        // list item will be indented but won't have a bullet/number to mimic HTML.
                        if(isFirstPara)
                        {
                            props.Add(new XElement(w + "numPr",
                                new XElement(w + "ilvl",
                                    new XAttribute(w + "val", level.ToString(CultureInfo.InvariantCulture))),
                                new XElement(w + "numId",
                                    new XAttribute(w + "val", id.ToString(CultureInfo.InvariantCulture)))));

                            isFirstPara = false;
                        }
                        else
                        {
                            // Each subsequent paragraph turns off contextual spacing and adds an appropriate
                            // indent.  Note that we're making an assumption here about the indent widths based
                            // on the default style sheet.
                            props.Add(
                                new XElement(w + "ind",
                                    new XAttribute(w + "left", ((level + 1) * 720).ToString(CultureInfo.InvariantCulture))),
                                new XElement(w + "contextualSpacing",
                                    new XAttribute(w + "val", "0")));
                        }
                    }
                }

                // Adjust the indent and width on any tables within the list item.  As above, we're making an
                // assumption about the indent widths based on the default style sheet.
                foreach(var para in listItem.Elements(w + "tbl"))
                {
                    var props = para.Element(w + "tblPr");

                    if(props == null)
                    {
                        props = new XElement(w + "tblPr");
                        para.AddFirst(props);
                    }

                    var indent = props.Element(w + "tblInd");

                    // Don't replace the indent in case it was set for a nested list
                    if(indent == null)
                    {
                        var insertLocation = props.Element(w + "tblW") ?? props.Element(w + "pStyle") ??
                            props.Elements().First();

                        insertLocation.AddAfterSelf(new XElement(w + "tblInd",
                            new XAttribute(w + "w", ((level + 1) * 720).ToString(CultureInfo.InvariantCulture)),
                            new XAttribute(w + "type", "dxa")));

                        props.Element(w + "tblW")?.Attribute(w + "w").Value =
                            (5000 - ((level + 1) * 430)).ToString(CultureInfo.InvariantCulture);
                    }
                }

                // Move all of the list item content up into the parent and remove the list item container
                foreach(var child in listItem.Nodes().ToList())
                {
                    child.Remove();
                    listItem.AddBeforeSelf(child);
                }

                listItem.Remove();
            }

            // Move all of the list content up into the parent and remove the list container
            foreach(var child in list.Nodes().ToList())
            {
                child.Remove();
                list.AddBeforeSelf(child);
            }

            list.Remove();
        }
        #endregion

        #region Compression methods and event handlers
        //=====================================================================

        /// <summary>
        /// This is used to generate the list of files needed for the document.  It also updates the content
        /// types file with any missing content types.
        /// </summary>
        private void GenerateFileList()
        {
            string altName, contentTypesFile = Path.Combine(workingFolder, "[Content_Types].xml"),
                relsFile = Path.Combine(workingFolder, "_rels", ".rels"),
                documentParts = Path.Combine(workingFolder, "word", "_rels", "document.xml.rels"), filename;

            archiveFiles.Add(contentTypesFile);
            archiveFiles.Add(relsFile);
            archiveFiles.Add(documentParts);

            // If the content types file does not exist, see if it's there under the alternate name used to
            // support the NuGet deployment.  Certain filenames reserved as part of the Open Packaging
            // Conventions are ignore by NuGet when extracting package files.  This works around the issue.
            if(!File.Exists(contentTypesFile))
            {
                altName = Path.Combine(workingFolder, "Content_Types.xml");

                if(File.Exists(altName))
                    File.Move(altName, contentTypesFile);
            }

            // Do the same for the relationships file
            if(!File.Exists(relsFile))
            {
                altName = Path.Combine(workingFolder, "_rels", "rels.xml_rels");

                if(File.Exists(altName))
                    File.Move(altName, relsFile);
            }

            XDocument contentTypes = XDocument.Load(contentTypesFile);

            foreach(var part in contentTypes.Descendants(ct + "Override"))
            {
                filename = part.Attribute("PartName").Value;

                if(filename[0] == '/')
                    filename = filename.Substring(1);

                filename = filename.CorrectFilePathSeparators();

                archiveFiles.Add(Path.GetFullPath(Path.Combine(workingFolder, filename)));
            }

            XDocument relParts = XDocument.Load(relsFile);
            XDocument docParts = XDocument.Load(documentParts);

            foreach(var rel in relParts.Descendants(pr + "Relationship").Concat(docParts.Descendants(
              pr + "Relationship")))
            {
                filename = rel.Attribute("Target").Value.CorrectFilePathSeparators();

                // Assume anything other than a hyperlink is a file of some sort
                if(!rel.Attribute("Type").Value.EndsWith("/hyperlink", StringComparison.Ordinal))
                {
                    try
                    {
                        // Adjust the file path as needed to make it correct for the archive list
                        if(filename.StartsWith(FilePath.RelativePathPrefix, StringComparison.Ordinal))
                            filename = filename.Substring(3);
                        else
                        {
                            if(!File.Exists(Path.Combine(workingFolder, filename)))
                                filename = Path.Combine("word", filename);
                        }

                        filename = Path.GetFullPath(Path.Combine(workingFolder, filename));

                        if(!File.Exists(filename))
                            buildProcess.ReportWarning("BOF0003", "Could not find referenced file: " + filename);
                        else
                            archiveFiles.Add(filename);
                    }
                    catch(Exception ex)
                    {
                        // Ignore anything that fails as it probably isn't a filename
                        System.Diagnostics.Debug.WriteLine(ex);
                    }
                }
            }

            // Add content type entries for any extensions not already in the [Content_Types].xml file.
            // This prevents corrupted documents.
            var extensions = new HashSet<string>(contentTypes.Descendants(ct + "Default").Select(
                d => "." + d.Attribute("Extension").Value), StringComparer.OrdinalIgnoreCase);
            var mimeTypes = XDocument.Parse(Properties.Resources.MimeTypes).Descendants("mimeType").Select(
                m => (m.Attribute("extension").Value, m.Attribute("type").Value)).ToDictionary(
                k => k.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

            foreach(var ext in archiveFiles.GroupBy(f => Path.GetExtension(f)).Where(f => f.Key.Length > 0 &&
              !extensions.Contains(f.Key)))
            {
                if(!mimeTypes.TryGetValue(ext.Key, out string mimeType))
                {
                    // Use unknown if not found
                    buildProcess.ReportWarning("BOF0004", "Unable to determine MIME type for extension '{0}'",
                        ext.Key);
                    mimeType = "application/unknown";
                }

                extensions.Add(ext.Key);

                contentTypes.Root.Add(new XElement(ct + "Default",
                    new XAttribute("Extension", ext.Key.Substring(1)),
                    new XAttribute("ContentType", mimeType)));
            }

            contentTypes.Save(contentTypesFile);
        }

        /// <summary>
        /// This is used to compress the help content into the Open XML document file
        /// </summary>
        private void CompressHelpContent()
        {
            progressProvider = new Progress<int>(ReportProgress);
            archiveName = Path.Combine(buildProcess.OutputFolder, buildProcess.CurrentProject.HtmlHelpName) + ".docx";

            buildProcess.ReportProgress("Compressing help content files into {0}", archiveName);

            var t = System.Threading.Tasks.Task.Run(() => this.CompressFiles());

            t.Wait();

            var archiveInfo = new FileInfo(archiveName);

            buildProcess.ReportProgress("Finished saving {0}\r\nCompressed {1} files.  Reduced size by " +
                "{2:N0} bytes ({3:N0}%).", archiveName, fileCount, uncompressedSize - archiveInfo.Length,
                (uncompressedSize != 0) ? 100.0 - (100.0 * archiveInfo.Length / uncompressedSize) : 100.0);
        }

        /// <summary>
        /// This is used to handle file compression as a background task
        /// </summary>
        private void CompressFiles()
        {
            int addCount = 0, baseFolderLength = workingFolder.Length;

            if(workingFolder[baseFolderLength - 1] != Path.DirectorySeparatorChar)
                baseFolderLength++;

            fileCount = archiveFiles.Count;

            using var archive = ZipFile.Open(archiveName, ZipArchiveMode.Create);
            
            // Compress just the files needed for the document.  Files are stored relative to the root.
            // We'll handle enumerating the files so that we can report progress.
            foreach(var file in archiveFiles)
            {
                string entryName = file.Substring(baseFolderLength).ToWebsiteOrZipFilePath();

                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);

                var fi = new FileInfo(file);

                addCount++;
                uncompressedSize += fi.Length;

                if((addCount % 500) == 0)
                    progressProvider.Report(addCount);
            }
        }

        /// <summary>
        /// Report progress as the content is compressed
        /// </summary>
        /// <param name="value">The number of files added to the archive so far</param>
        private void ReportProgress(int value)
        {
            buildProcess.ReportProgress("Saved {0} of {1} items", value, fileCount);
        }
        #endregion
    }
}

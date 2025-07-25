﻿//===============================================================================================================
// System  : Sandcastle Tools Standard Presentation Styles
// File    : VisualStudio2013PresentationStyle.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 07/08/2025
// Note    : Copyright 2014-2025, Eric Woodruff, All rights reserved
//
// This file contains the presentation style definition for the Visual Studio 2013 presentation style.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 04/13/2014  EFW  Created the code
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Sandcastle.Core;
using Sandcastle.Core.PresentationStyle;
using Sandcastle.Core.Project;

namespace Sandcastle.PresentationStyles.VS2013
{
    /// <summary>
    /// This contains the definition for the Visual Studio 2013 presentation style
    /// </summary>
    [PresentationStyleExport("VS2013", "VS2013", Version = AssemblyInfo.ProductVersion,
      Copyright = AssemblyInfo.Copyright, Description = "This presentation style is similar to the one used for " +
        "Visual Studio 2013 offline content and the now retired MSDN online content.")]
    public sealed class VisualStudio2013PresentationStyle : PresentationStyleSettings
    {
        /// <inheritdoc />
        public override string Location => ComponentUtilities.AssemblyFolder(Assembly.GetExecutingAssembly());

        /// <summary>
        /// Constructor
        /// </summary>
        public VisualStudio2013PresentationStyle()
        {
            // The base path of the presentation style files relative to the assembly's location
            this.BasePath = "VS2013";

            this.SupportedFormats = HelpFileFormats.HtmlHelp1 | HelpFileFormats.MSHelpViewer |
                HelpFileFormats.Website;

            this.SupportsNamespaceGrouping = this.SupportsCodeSnippetGrouping = true;

            // This is the legacy format and requires the HTML extract build step for website output
            this.RequiresHtmlExtractBuildStep = true;

            this.DocumentModelApplicator = new StandardDocumentModel();
            this.ApiTableOfContentsGenerator = new StandardApiTocGenerator();
            this.TopicTransformation = new VisualStudio2013Transformation(this.SupportedFormats, this.ResolvePath);

            // If relative, these paths are relative to the base path
            this.BuildAssemblerConfiguration = Path.Combine("Configuration", "BuildAssembler.config");

            // Note that UNIX based web servers may be case-sensitive with regard to folder and filenames so
            // match the case of the folder and filenames in the literals to their actual casing on the file
            // system.
            this.ContentFiles.Add(new ContentFiles(this.SupportedFormats, Path.Combine("icons", "*.*")));
            this.ContentFiles.Add(new ContentFiles(this.SupportedFormats, Path.Combine("scripts", "*.*")));
            this.ContentFiles.Add(new ContentFiles(this.SupportedFormats, Path.Combine("styles", "*.*")));
            this.ContentFiles.Add(new ContentFiles(HelpFileFormats.Website, null,
                Path.Combine("Web", "*.*"), String.Empty, [".aspx", ".html", ".htm", ".php"]));

            // Add the plug-in dependencies
            this.PlugInDependencies.Add(new PlugInDependency("Lightweight Website Style", null));
        }

        /// <inheritdoc />
        /// <remarks>This presentation style only uses the standard shared content</remarks>
        public override IEnumerable<string> ResourceItemFiles(string languageName)
        {
            string filePath = this.ResolvePath(Path.Combine("..", "Shared", "Content")),
                fileSpec = "SharedContent_" + languageName + ".xml";

            if(!File.Exists(Path.Combine(filePath, fileSpec)))
                fileSpec = "SharedContent_en-US.xml";

            yield return Path.Combine(filePath, fileSpec);

            foreach(string f in this.AdditionalResourceItemsFiles)
                yield return f;
        }
    }
}

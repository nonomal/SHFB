//===============================================================================================================
// System  : Sandcastle Help File Builder MSBuild Tasks
// File    : SandcastleProject.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 07/27/2025
// Note    : Copyright 2006-2025, Eric Woodruff, All rights reserved
//
// This file contains the project class.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
// Version     Date     Who  Comments
// ==============================================================================================================
#region History
// 1.0.0.0  08/03/2006  EFW  Created the code
// 1.1.0.0  08/28/2006  EFW  Added various new options for the August CTP
// 1.2.0.0  09/04/2006  EFW  Added new properties to support namespace summaries and project summary comments
// 1.3.1.0  09/26/2006  EFW  Added the ShowMissing* properties
// 1.3.1.0  10/02/2006  EFW  Added support for the September CTP and the Document* properties
// 1.3.2.0  11/03/2006  EFW  Added the NamingMethod property
// 1.3.3.1  11/24/2006  EFW  Added the SyntaxFilters property, support for build component configurations, and
//                           other stuff.
// 1.3.4.0  12/24/2006  EFW  Added WorkingPath project property.  Reworked the load and save code to use
//                           reflection for most properties to simplify the code and support setting them via the
//                           command line from the console mode builder.  Converted folder properties to
//                           FolderPath objects.
// 1.4.0.0  02/02/2007  EFW  Converted PresentationStyle to a string with a type converter listing the
//                           presentation folders.  Added FooterText property.
// 1.4.0.2  05/11/2007  EFW  Missing namespace messages are now optional
// 1.5.0.2  07/03/2007  EFW  Added support to additional content site map
// 1.5.1.0  07/20/2007  EFW  Added ApiFilter and KeepLogFile project properties
// 1.5.1.0  08/24/2007  EFW  Added support for the inherited private/internal framework member flags
// 1.5.2.0  09/10/2007  EFW  Added support for plug-in configurations
// 1.6.0.0  09/30/2007  EFW  Added support for transforming *.topic files
// 1.6.0.1  10/14/2007  EFW  Added support for ShowFeedbackControl and SdkLinkTarget
// 1.6.0.2  11/01/2007  EFW  Reworked to support better handling of components
// 1.6.0.5  02/20/2008  EFW  Added the FeedbackEMailLinkText property
// 1.6.0.7  03/21/2008  EFW  Added Help 2 and ShowMissingTypeParam properties.  Removed the PurgeDuplicateTopics
//                           option.  Added the BuildLogFile property.  Started laying the foundations for
//                           conceptual content support.  ContentSiteMap and TopicFileTransform were made
//                           sub-properties of the additional content collection to connect them with it and to
//                           prevent associating them with conceptual content.
// 1.8.0.0  06/20/2008  EFW  Converted the project to use an MSBuild file.
// 1.8.0.1  12/14/2008  EFW  Updated for use with .NET 3.5 and MSBuild 3.5.  Added support for user-defined
//                           project properties.  Added support for ShowMissingIncludeTargets.
// 1.8.0.3  07/05/2009  EFW  Added support for MS Help Viewer format
// 1.8.0.3  11/10/2009  EFW  Changed SyntaxFilters property to a string to support custom syntax filter build
//                           components.
// 1.8.0.3  11/19/2009  EFW  Added support for AutoDocumentDisposeMethods
// 1.8.0.3  12/06/2009  EFW  Removed support for ShowFeedbackControl
// 1.9.0.0  06/19/2010  EFW  Added properties to support MS Help Viewer.  Removed ProjectLinkType property.
//                           Replaced SdkLinkType with help format specific SDK link type properties.
// 1.9.1.0  07/09/2010  EFW  Updated for use with .NET 4.0 and MSBuild 4.0
// 1.9.2.0  01/16/2011  EFW  Updated to support selection of Silverlight Framework versions
// 1.9.3.2  08/20/2011  EFW  Updated to support selection of .NET Portable Framework versions
// 1.9.4.0  04/08/2012  EFW  Merged changes for VS2010 style from Don Fehr.  Added BuildAssemblerVerbosity
//                           property.  Added Support for XAML configuration files.
// 1.9.5.0  09/10/2012  EFW  Updated to use the new framework definition file for the .NET Framework versions.
//                           Added the CatalogName property for Help Viewer 2.0 support.
// 1.9.6.0  10/13/2012  EFW  Removed the BrandingPackageName and SelfBranded properties.  Added support for
//                           transform component arguments.
// 1.9.9.0  11/30/2013  EFW  Merged changes from Stazzz to support namespace grouping
// -------  12/17/2013  EFW  Removed the SandcastlePath property and all references to it
//          12/20/2013  EFW  Added support for the ComponentPath project property
//          05/03/2015  EFW  Removed support for the MS Help 2 file format
//          01/22/2016  EFW  Added SaveComponentCacheCapacity property
//          08/25/2016  EFW  Added support for the SourceCodeBasePath property
//          09/22/2017  EFW  Added support for EditorBrowsable and Browsable attribute visibility settings
//          12/10/2017  EFW  Added support for the WebsiteAdContent and SearchResultsDisplayVersion properties
#endregion
//===============================================================================================================

// Ignore Spelling: Fehr Stazzz param typeparam safeprojectname apifilter documentationsources namespacesummaries
// Ignore Spelling: componentconfigurations pluginconfigurations transformcomponentarguments helpfileformat
// Ignore Spelling: frameworkversion presentationstyle visibleitems

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

using Microsoft.Build.Evaluation;

using Sandcastle.Core;
using Sandcastle.Core.BuildAssembler;
using Sandcastle.Core.BuildAssembler.BuildComponent;
using Sandcastle.Core.ConceptualContent;
using Sandcastle.Core.Reflection;
using Sandcastle.Core.PlugIn;
using Sandcastle.Core.Project;
using Sandcastle.Core.PresentationStyle.Transformation;

using Sandcastle.Core.BuildEngine;
using SandcastleBuilder.MSBuild.Design;
using SandcastleBuilder.MSBuild.BuildEngine;

namespace SandcastleBuilder.MSBuild.HelpProject
{
    /// <summary>
    /// This class represents all of the properties that make up a Sandcastle Help File Builder project
    /// </summary>
    public sealed class SandcastleProject : ISandcastleProject
    {
        #region Constants
        //=====================================================================

        // NOTE: When this changes, update the version in Resources\ProjectTemplate.txt and in the
        //       SancastleBuilderPackage project templates.
        /// <summary>
        /// The schema version used in the saved project files
        /// </summary>
        public static readonly Version SchemaVersion = new(2017, 9, 26, 0);

        // This is used to convert MSBuild variable references to normal environment variable references
        private static readonly Regex reMSBuildVar = new(@"\$\((.*?)\)");

        #endregion

        #region Private data members
        //=====================================================================

        // Bad characters for the vendor name property
        private static readonly Regex reBadVendorNameChars = new(@"[:\\/\.,#&]");

        // These are used to decode hex values in the copyright text
        private static readonly Regex reDecode = new(@"\\x[0-9a-f]{2,4}", RegexOptions.IgnoreCase);

        private readonly MatchEvaluator characterMatchEval, buildVarMatchEval;

        // MS Build and property items
        private readonly Project msBuildProject;
        private Dictionary<string, ProjectProperty> projectPropertyCache;  // MSBuild property cache
        private readonly bool removeProjectWhenDisposed;

        // Local property info cache
        private static Dictionary<string, PropertyInfo> propertyCache = InitializePropertyCache();
        private static PropertyDescriptorCollection pdcCache;

        // Collection valued properties
        private DocumentationSourceCollection docSources;
        private ApiFilterCollection apiFilter;
        private NamespaceSummaryItemCollection namespaceSummaries;
        private ComponentConfigurationDictionary componentConfigs;
        private PlugInConfigurationDictionary plugInConfigs;

        #endregion

        #region Miscellaneous properties
        //=====================================================================

        /// <summary>
        /// This returns a collection of restricted property names that cannot be used for user-defined property
        /// names.
        /// </summary>
        public static IReadOnlyCollection<string> RestrictedProperties { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "AssemblyName", "Configuration", "CustomAfterSHFBTargets", "CustomBeforeSHFBTargets",
                "DumpLogOnFailure", "Name", "Platform", "PostBuildEvent", "PreBuildEvent", "ProjectGuid",
                "RootNamespace", "RunPostBuildEvent", "SccAuxPath", "SccLocalPath", "SccProjectName",
                "SccProvider", "SchemaVersion", "SHFBSchemaVersion", "TargetFrameworkVersion",
                "TransformComponentArguments", "Verbose" };

        /// <summary>
        /// This read-only property returns the MSBuild project property cache
        /// </summary>
        /// <value>There can be duplicate versions of the properties so this picks the last one as it will
        /// contain the value to use.  Property names are case-insensitive.</value>
        private Dictionary<string, ProjectProperty> ProjectPropertyCache =>
            projectPropertyCache ??= msBuildProject.AllEvaluatedProperties.GroupBy(
                    p => p.Name.ToLowerInvariant()).Select(g => g.Last()).ToDictionary(
                        p => p.Name, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// This read-only property is used to get the underlying MSBuild project
        /// </summary>
        public Project MSBuildProject => msBuildProject;

        /// <summary>
        /// This read-only property is used to get whether or not the project is using final values for the
        /// project properties.
        /// </summary>
        /// <value>If true, final values (i.e. evaluated values used at build time) are being returned by the
        /// properties in this instance.</value>
        [XmlIgnore]
        public bool UsingFinalValues { get; }

        /// <summary>
        /// This read-only property is used to get the filename for the project
        /// </summary>
        public string Filename => msBuildProject.FullPath;

        /// <summary>
        /// This is used to get or set the configuration to use when building the project
        /// </summary>
        /// <value>This value is used for project documentation sources and project references so that the
        /// correct items are used from them.</value>
        [XmlIgnore]
        public string Configuration
        {
            get
            {
                string config = null;

                if(msBuildProject != null)
                {
                    if(!msBuildProject.GlobalProperties.TryGetValue(BuildItemMetadata.Configuration, out config))
                        config = null;
                }

                if(String.IsNullOrEmpty(config))
                    config = BuildItemMetadata.DefaultConfiguration;

                return config;
            }
            set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = BuildItemMetadata.DefaultConfiguration;

                msBuildProject.SetGlobalProperty(BuildItemMetadata.Configuration, value);
                msBuildProject.ReevaluateIfNecessary();
            }
        }

        /// <summary>
        /// This is used to get or set the platform to use when building the project
        /// </summary>
        /// <value>This value is used for project documentation sources and project references so that the
        /// correct items are used from them.</value>
        [XmlIgnore]
        public string Platform
        {
            get
            {
                string platform = null;

                if(msBuildProject != null)
                {
                    if(!msBuildProject.GlobalProperties.TryGetValue(BuildItemMetadata.Platform, out platform))
                        platform = null;
                }

                if(String.IsNullOrEmpty(platform))
                    platform = BuildItemMetadata.DefaultPlatform;

                return platform;
            }
            set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = BuildItemMetadata.DefaultPlatform;

                msBuildProject.SetGlobalProperty(BuildItemMetadata.Platform, value);
                msBuildProject.ReevaluateIfNecessary();
            }
        }

        /// <summary>
        /// This is used to get or set the MSBuild <c>OutDir</c> property value that is defined when using Team
        /// Build.
        /// </summary>
        /// <value>This value is used for project documentation sources and project references so that the
        /// correct items are used from them.</value>
        [XmlIgnore]
        public string MSBuildOutDir
        {
            get
            {
                string outDir = null;

                if(msBuildProject != null)
                {
                    // Ignore ".\" as that's our default.
                    if(msBuildProject.GlobalProperties.TryGetValue(BuildItemMetadata.OutDir, out outDir))
                    {
                        if(outDir == FilePath.DefaultOutDir)
                            outDir = null;
                    }
                }

                return outDir;
            }
            set
            {
                msBuildProject.SetGlobalProperty(BuildItemMetadata.OutDir, (value ?? String.Empty).Trim());
                msBuildProject.ReevaluateIfNecessary();
            }
        }

        /// <summary>
        /// This read-only property is used to get the MSBuild-specific <c>OutDir</c> property value 
        /// </summary>
        public string ProjectOutDir => msBuildProject.GetProperty("OutDir")?.EvaluatedValue;

        /// <summary>
        /// This is used to get the dirty state of the project
        /// </summary>
        public bool IsDirty => msBuildProject.Xml.HasUnsavedChanges;

        /// <summary>
        /// This read-only property is used to get the build log file location
        /// </summary>
        /// <value>If <see cref="BuildLogFile"/> is set, it returns its value.  If not set, it returns the full
        /// path created by using the <see cref="OutputPath"/> property value and a filename of
        /// <strong>LastBuild.log</strong>.</value>
        public string LogFileLocation
        {
            get
            {
                string path;

                if(this.BuildLogFile.Path.Length != 0)
                    return this.BuildLogFile.ToString();

                if(Path.IsPathRooted(this.OutputPath))
                    path = this.OutputPath;
                else
                    path = Path.Combine(Path.GetDirectoryName(msBuildProject.FullPath), this.OutputPath);

                return Path.GetFullPath(path + "LastBuild.log");
            }
        }

        /// <summary>
        /// This read-only property returns an enumerable list of documentation sources to use in building the
        /// help file.
        /// </summary>
        public IEnumerable<IDocumentationSource> DocumentationSources =>
            docSources ??= new DocumentationSourceCollection(this);

        /// <summary>
        /// This returns an enumerable list of all build items in the project that represent folders and files
        /// </summary>
        public IEnumerable<IFileItem> FileItems
        {
            get
            {
                List<string> buildActions = [.. Enum.GetNames(typeof(BuildAction))];

                foreach(ProjectItem item in msBuildProject.AllEvaluatedItems)
                {
                    if(buildActions.IndexOf(item.ItemType) != -1)
                        yield return new FileItem(this, item);
                }
            }
        }

        /// <summary>
        /// This read-only property returns an enumerable list of image references contained in the project
        /// </summary>
        /// <returns>An enumerable list of image references if any are found in the project</returns>
        /// <remarks>Only images with IDs are returned</remarks>
        public IEnumerable<ImageReference> ImagesReferences
        {
            get
            {
                ImageReference imageRef;
                string id;

                foreach(ProjectItem item in msBuildProject.GetItems(BuildAction.Image.ToString()))
                {
                    id = item.GetMetadataValue(BuildItemMetadata.ImageId);

                    if(!String.IsNullOrWhiteSpace(id))
                    {
                        imageRef = new ImageReference(new FilePath(item.UnevaluatedInclude, this), id)
                        {
                            AlternateText = item.GetMetadataValue(BuildItemMetadata.AlternateText)
                        };

                        if(!Boolean.TryParse(item.GetMetadataValue(BuildItemMetadata.CopyToMedia), out bool copyToMedia))
                            copyToMedia = false;

                        imageRef.CopyToMedia = copyToMedia;

                        yield return imageRef;
                    }
                }
            }
        }

        /// <summary>
        /// This returns an enumerable list of transform component arguments
        /// </summary>
        /// <remarks>These are passed as arguments to the XSL transformations used by the <b>BuildAssembler</b>
        /// <c>TransformComponent</c>.</remarks>
        /// <returns>An enumerable list of transform component arguments</returns>
        public IEnumerable<TransformationArgument> TransformComponentArguments
        {
            get
            {
                var argsProp = msBuildProject.GetProperty("TransformComponentArguments");

                if(argsProp != null && !String.IsNullOrEmpty(argsProp.UnevaluatedValue))
                {
                    // Use a reader to ignore namespaces
                    using var xr = new XmlTextReader("<Args>" + argsProp.UnevaluatedValue + "</Args>",
                      XmlNodeType.Element, new XmlParserContext(null, null, null, XmlSpace.Preserve))
                    {
                        Namespaces = false 
                    };
                    
                    xr.MoveToContent();

                    foreach(var arg in XElement.Load(xr, LoadOptions.PreserveWhitespace).Descendants("Argument"))
                        yield return new TransformationArgument(arg);
                }
            }
        }

        /// <summary>
        /// This read-only property returns an enumerable list of all user-defined property names and values
        /// </summary>
        /// <returns>An enumerable list of all properties and their values determined not to be help file builder
        /// project properties, MSBuild build engine related properties, or environment variables.</returns>
        public IEnumerable<(string Name, string Value)> UserDefinedProperties
        {
            get
            {
                if(msBuildProject != null && propertyCache != null)
                {
                    foreach(ProjectProperty prop in msBuildProject.AllEvaluatedProperties)
                    {
                        if(!prop.IsEnvironmentProperty && !prop.IsGlobalProperty && !prop.IsImported &&
                          !prop.IsReservedProperty && !propertyCache.ContainsKey(prop.Name) &&
                          !RestrictedProperties.Contains(prop.Name))
                        {
                            yield return (prop.Name, prop.EvaluatedValue);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This read-only property returns an enumerable list of all user-defined project properties
        /// </summary>
        /// <returns>An enumerable list of all properties determined not to be help file builder project
        /// properties, MSBuild build engine related properties, or environment variables.</returns>
        public IEnumerable<ProjectProperty> UserDefinedProjectProperties
        {
            get
            {
                if(msBuildProject != null && propertyCache != null)
                {
                    foreach(ProjectProperty prop in msBuildProject.AllEvaluatedProperties)
                    {
                        if(!prop.IsEnvironmentProperty && !prop.IsGlobalProperty && !prop.IsImported &&
                          !prop.IsReservedProperty && !propertyCache.ContainsKey(prop.Name) &&
                          !RestrictedProperties.Contains(prop.Name))
                        {
                            yield return prop;
                        }
                    }
                }
            }
        }
        #endregion

        #region Project and namespace summary properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get the project summary comments
        /// </summary>
        /// <remarks>These notes will appear in the root namespaces page if entered</remarks>
        [EscapeValue]
        public string ProjectSummary
        {
            get => field;
            set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property returns the list of namespace summaries
        /// </summary>
        public NamespaceSummaryItemCollection NamespaceSummaries
        {
            get
            {
                if(namespaceSummaries == null)
                {
                    namespaceSummaries = [];

                    var nsProperty = msBuildProject.GetProperty("NamespaceSummaries");

                    if(nsProperty != null && !String.IsNullOrWhiteSpace(nsProperty.UnevaluatedValue))
                        namespaceSummaries.FromXml(nsProperty.UnevaluatedValue);
                }

                return namespaceSummaries;
            }
        }
        #endregion

        #region Path properties
        //=====================================================================

        /// <summary>
        /// This property is used to get or set the path to a folder containing additional, project-specific
        /// build components.
        /// </summary>
        /// <value>If left blank, the current project's folder is searched instead</value>
        public FolderPath ComponentPath
        {
            get => field;
            set
            {
                if(value == null)
                    value = new FolderPath(this);

                field = value;
            }
        }

        /// <summary>
        /// This property is used to get or set the base path used to locate source code for the documented
        /// assemblies.
        /// </summary>
        /// <value>If left blank, source context information will be omitted from the reflection data</value>
        public FolderPath SourceCodeBasePath
        {
            get => field;
            set
            {
                if(value == null)
                    value = new FolderPath(this);

                field = value;
            }
        }

        /// <summary>
        /// This is used to get or set whether or not to issue a warning if a source code context could not be
        /// determined for a type.
        /// </summary>
        /// <value>This is false by default and missing source context issues will be reported as informational
        /// messages.  If set to true, they are reported as warnings that MSBuild will also report.</value>
        public bool WarnOnMissingSourceContext { get; set; }

        /// <summary>
        /// This property is used to get or set the path to the HTML Help 1 compiler (HHC.EXE)
        /// </summary>
        /// <value>You only need to set this if the builder cannot determine the path for itself</value>
        public FolderPath HtmlHelp1xCompilerPath
        {
            get => field;
            set
            {
                if(value == null)
                    value = new FolderPath(this);

                field = value;
            }
        }

        /// <summary>
        /// This property is used to get or set the path to which the help files will be generated
        /// </summary>
        /// <remarks><para>The default is to create it in a folder called <strong>.\Help</strong> in the same
        /// folder as the project file.</para>
        /// 
        /// <para><strong>Warning:</strong> If building a web site, the output folder's prior content will be
        /// erased without warning prior to copying the new web site content to it!</para></remarks>
        public string OutputPath
        {
            get => field;
            set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = @".\Help\";
                else
                    value = FolderPath.TerminatePath(value);

                field = value;
            }
        }

        /// <summary>
        /// This is used to get or set the path to the working folder used during the build process to store the
        /// intermediate files.
        /// </summary>
        /// <value><para>This can be used to perform the build in a different location with a shorter path if you
        /// encounter errors due to long file path names.  If not specified, it defaults to a folder called
        /// <strong>.\Working</strong> under the folder specified by the <see cref="OutputPath"/> property.</para>
        /// 
        /// <para><strong>Warning:</strong> All files and folders in the path specified in this property will be
        /// erased without warning when the build starts.</para></value>
        public FolderPath WorkingPath
        {
            get => field;
            set
            {
                if(value == null)
                    value = new FolderPath(this);

                field = value;
            }
        }

        /// <summary>
        /// This read-only property returns an enumerable list of folders to search for additional build
        /// components, plug-ins, presentation styles, and syntax generators.
        /// </summary>
        public IEnumerable<string> ComponentSearchPaths
        {
            get
            {
                // Components from NuGet packages should have set a SHFBComponentPath item so that we can find
                // them.  These are always searched first.
                foreach(var cp in msBuildProject.GetItems("SHFBComponentPath"))
                    yield return cp.EvaluatedInclude;

                // Components in the component path will override those if duplicates are found.  Only return it
                // if it isn't the project path.
                string projectPath = Path.GetDirectoryName(msBuildProject.FullPath);

                if(!String.IsNullOrWhiteSpace(this.ComponentPath) &&
                  !projectPath.Equals(this.ComponentPath, StringComparison.OrdinalIgnoreCase))
                {
                    yield return this.ComponentPath;
                }
                    
                // And finally, components in the current project path will override all of the above if
                // duplicates are found.
                yield return projectPath;
            }
        }
        #endregion

        #region Build properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get the build assembler tool verbosity level
        /// </summary>
        /// <value>The default is <c>AllMessages</c> to report all messages</value>
        /// <remarks>Setting this property to <c>OnlyWarningsAndErrors</c> or <c>OnlyErrors</c> can
        /// significantly reduce the size of the build log for large projects.</remarks>
        public BuildAssemblerVerbosity BuildAssemblerVerbosity { get; private set; }

        /// <summary>
        /// This property is used to get or set whether intermediate files are deleted after a successful build
        /// </summary>
        /// <value>The default value is true</value>
        public bool CleanIntermediates { get; set; }

        /// <summary>
        /// This property is used to get or set whether or not the log file is retained after a successful build
        /// </summary>
        /// <value>The default value is true</value>
        public bool KeepLogFile { get; set; }

        /// <summary>
        /// This read-only property is used to get the path and filename of the build log file
        /// </summary>
        /// <value>If not specified, a default name of <strong>LastBuild.log</strong> is used and the file is
        /// saved in the path identified in the <see cref="OutputPath" /> property.</value>
        public FilePath BuildLogFile
        {
            get => field;
            private set
            {
                if(value == null)
                    value = new FilePath(this);

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the help file format generated by the build process
        /// </summary>
        /// <value>The default is to produce an HTML Help 1 format file</value>
        /// <remarks>If building a web site, the output folder will be cleared before the new content is copied
        /// to it.</remarks>
        public HelpFileFormats HelpFileFormat { get; private set; }

        /// <summary>
        /// This read-only property is used to get whether or not to disable the custom Code Block Component so
        /// that <c>&lt;code&gt;</c> elements are rendered in their standard format by the Sandcastle XSL
        /// transformations.
        /// </summary>
        /// <value>The default is false so that the Code Block Component is used by default</value>
        public bool DisableCodeBlockComponent { get; private set; }

        /// <summary>
        /// This is used to get or set the .NET Framework version used to resolve references to system types
        /// (basic .NET Framework, Silverlight, Portable, etc.).
        /// </summary>
        /// <remarks>If set to null, it will default to the most recent version of the basic .NET Framework
        /// installed.  The build engine will adjust this at build time if necessary based on the framework
        /// types and versions found in the documentation sources.</remarks>
        [EscapeValue]
        public string FrameworkVersion
        {
            get => field;
            set
            {
                // Let bad values through.  The property pages or the build engine will catch bad values if
                // necessary.
                if(String.IsNullOrWhiteSpace(value))
                    value = ReflectionDataSetDictionary.DefaultFrameworkTitle;

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get whether or not the HTML rendered by <strong>BuildAssembler</strong>
        /// is indented.
        /// </summary>
        /// <value>This is mainly a debugging aid.  Leave it set to false, the default, to produce more compact
        /// HTML.</value>
        public bool IndentHtml { get; private set; }

        /// <summary>
        /// This read-only property is used to get the build assembler Save Component writer task cache capacity
        /// </summary>
        /// <value>The default is 100 to limit the cache to 100 entries</value>
        /// <remarks>Decrease the value to conserve memory, increase it to help with build speed at the expense
        /// of memory used.  Set it to zero to allow an unbounded cache for the writer task (best speed at the
        /// expense of memory used).</remarks>
        public int SaveComponentCacheCapacity { get; private set; }

        /// <summary>
        /// This read-only property is used to get a dictionary of build component configurations
        /// </summary>
        /// <remarks>This allows you to configure the settings for third party build components if they
        /// support it.</remarks>
        public ComponentConfigurationDictionary ComponentConfigurations
        {
            get
            {
                if(componentConfigs == null)
                {
                    componentConfigs = [];

                    var compConfigProperty = msBuildProject.GetProperty("ComponentConfigurations");

                    if(compConfigProperty != null && !String.IsNullOrWhiteSpace(compConfigProperty.UnevaluatedValue))
                        componentConfigs.FromXml(compConfigProperty.UnevaluatedValue);
                }

                return componentConfigs;
            }
        }

        /// <summary>
        /// This read-only property is used to get a dictionary of build process plug-in configurations
        /// </summary>
        /// <remarks>This allows you to select and configure the settings for third party build process plug-ins</remarks>
        public PlugInConfigurationDictionary PlugInConfigurations
        {
            get
            {
                if(plugInConfigs == null)
                {
                    plugInConfigs = [];

                    var plugInConfigProperty = msBuildProject.GetProperty("PlugInConfigurations");

                    if(plugInConfigProperty != null && !String.IsNullOrWhiteSpace(plugInConfigProperty.UnevaluatedValue))
                        plugInConfigs.FromXml(plugInConfigProperty.UnevaluatedValue);
                }

                return plugInConfigs;
            }
        }
        #endregion

        #region Help file properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get the placement of any additional and conceptual content items
        /// in the table of contents.
        /// </summary>
        /// <value>The default is to place additional and conceptual content items above the namespaces</value>
        public ContentPlacement ContentPlacement { get; private set; }

        /// <summary>
        /// This read-only property is used to get whether or not all pages should be marked with a "preliminary
        /// documentation" warning in the page header.
        /// </summary>
        public bool Preliminary { get; private set; }

        /// <summary>
        /// This read-only property is used to get whether or not a root namespace entry is added to the table of
        /// contents to act as a container for the namespaces from the documented assemblies.
        /// </summary>
        /// <value>If true, a root <strong>Namespaces</strong> table of contents entry will be created as the
        /// container of the namespaces in the documented assemblies.  If false, the default, the namespaces are
        /// listed in the table of contents as root entries.</value>
        public bool RootNamespaceContainer { get; private set; }

        /// <summary>
        /// This read-only property is used to get an alternate title for the root namespaces page and the root
        /// table of contents container that appears when <see cref="RootNamespaceContainer"/> is set to true.
        /// </summary>
        /// <value>If left blank (the default), the localized version of the text "Namespaces" will be used</value>
        [EscapeValue]
        public string RootNamespaceTitle
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get whether namespace grouping is enabled.  The presentation style
        /// must have support for namespace grouping in order for the feature to work.
        /// </summary>
        /// <value>If <c>true</c>, namespace grouping is enabled. Otherwise, namespace grouping is not enabled</value>
        /// <remarks>Namespace groups are determined automatically and may be documented as well</remarks>
        public bool NamespaceGrouping { get; private set; }

        /// <summary>
        /// This read-only property is used to get the maximum number of namespace parts to consider when
        /// namespace grouping is enabled.
        /// </summary>
        /// <value>The minimum and default is 2.  A higher value results in more namespace groups</value>
        /// <remarks>Namespace groups are determined automatically and may be documented as well</remarks>
        public int MaximumGroupParts
        {
            get => field;
            private set
            {
                if(value < 2)
                    value = 2;

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get or set the help file's title
        /// </summary>
        [EscapeValue]
        public string HelpTitle
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "A Sandcastle Documented Class Library";
                else
                    value = value.Trim();

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the name of the compiled help file
        /// </summary>
        /// <remarks>Do not include a path or the extension.  For MS Help Viewer builds, avoid periods,
        /// ampersands, and pound signs as they are not valid in the help file name.</remarks>
        [EscapeValue]
        public string HtmlHelpName
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "Documentation";
                else
                    value = value.Trim();

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the version number applied to the help file
        /// </summary>
        /// <remarks>The default is 1.0.0.0</remarks>
        [EscapeValue]
        public string HelpFileVersion
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "1.0.0.0";
                else
                    value = value.Trim();

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the language option for the help file and to determine which
        /// set of presentation resource files to use.
        /// </summary>
        /// <value>If a matching set of presentation resources cannot be found for the specified language, the
        /// US English set will be used.</value>
        /// <remarks>The MS Help Viewer 1.0 Catalog ID is composed of the <see cref="CatalogProductId"/>, the
        /// <see cref="CatalogVersion"/>, and the <c>Language</c> code. For example, the English Visual Studio 10
        /// catalog is <c>VS_100_EN-US</c>.</remarks>
        public CultureInfo Language
        {
            get => field;
            private set
            {
                if(value == null || value == CultureInfo.InvariantCulture)
                    value = new CultureInfo("en-US");

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the URL to use as the link for the copyright notice
        /// </summary>
        /// <value>If not set, the <see cref="CopyrightText"/> (if any) is not turned into a clickable link</value>
        [EscapeValue]
        public string CopyrightHref
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get the copyright notice that appears in the footer of each page
        /// </summary>
        /// <remarks>If not set, no copyright note will appear.  If a <see cref="CopyrightHref" /> is specified
        /// without copyright text, the URL appears instead.</remarks>
        [EscapeValue]
        public string CopyrightText
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get the copyright notice that appears in the footer of each page
        /// with any hex value place holders replaced with their actual character.
        /// </summary>
        public string DecodedCopyrightText => reDecode.Replace(this.CopyrightText, characterMatchEval);

        /// <summary>
        /// This read-only property is used to get the feedback e-mail address that appears in the footer of each
        /// page.
        /// </summary>
        /// <remarks>If not set, no feedback link will appear.  If <see cref="FeedbackEMailLinkText"/> is set,
        /// that text will appear as the text for the link.  If not set, the e-mail address is used as the link
        /// text.</remarks>
        [EscapeValue]
        public string FeedbackEMailAddress
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get the feedback e-mail link text that appears in the feedback
        /// e-mail link in the footer of each page.
        /// </summary>
        /// <remarks>If set, this text will appear as the link text for the <see cref="FeedbackEMailAddress"/>
        /// link.  If not set, the e-mail address is used for the link text.</remarks>
        [EscapeValue]
        public string FeedbackEMailLinkText
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get additional text that should appear in the header of every page
        /// </summary>
        [EscapeValue]
        public string HeaderText
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get additional text that should appear in the footer of every page
        /// </summary>
        [EscapeValue]
        public string FooterText
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get the target window for external SDK links
        /// </summary>
        /// <value>The default is <c>Blank</c> to open the topics in a new window.  This option only has an
        /// effect on the <see cref="HtmlSdkLinkType"/>, <see cref="MSHelpViewerSdkLinkType"/>, and
        /// <see cref="WebsiteSdkLinkType"/> properties if they are set to <c>Msdn</c>.</value>
        public SdkLinkTarget SdkLinkTarget { get; private set; }

        /// <summary>
        /// This read-only property is used to get the presentation style for the help topic pages
        /// </summary>
        /// <value>The default is defined by <see cref="Constants.DefaultPresentationStyle" qualifyHint="true" /></value>
        [EscapeValue]
        public string PresentationStyle
        {
            get => field;
            private set
            {
                // Let bad values through.  The property pages or the build engine will catch bad values if
                // necessary.
                if(String.IsNullOrWhiteSpace(value))
                    value = Constants.DefaultPresentationStyle;

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the naming method used to generate the help topic filenames
        /// </summary>
        /// <value>The default is to use GUID values as the filenames</value>
        public NamingMethod NamingMethod { get; private set; }

        /// <summary>
        /// This read-only property is used to get the language filters which determines which languages appear
        /// in the <strong>Syntax</strong> section of the help topics.
        /// </summary>
        /// <value>The default is <strong>Standard</strong> (C#, VB.NET, and C++)</value>
        public string SyntaxFilters
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "Standard";

                field = value;
            }
        }
        #endregion

        #region HTML Help 1 properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get whether or not to create a binary table of contents in Help 1
        /// files.
        /// </summary>
        /// <remarks>This can significantly reduce the amount of time required to load a very large help file</remarks>
        public bool BinaryTOC { get; private set; }

        /// <summary>
        /// This read-only property is used to get the type of links used to reference other help topics
        /// referring to framework (SDK) help topics in HTML Help 1 help files.
        /// </summary>
        /// <value>The default is to produce links to online content</value>
        public HtmlSdkLinkType HtmlSdkLinkType { get; private set; }

        /// <summary>
        /// This read-only property is used to get whether or not a Favorites tab will appear in the help file
        /// </summary>
        public bool IncludeFavorites { get; private set; }

        #endregion

        #region MS Help Viewer properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get the type of links used to reference other help topics
        /// referring to framework (SDK) help topics in MS Help Viewer help files.
        /// </summary>
        /// <value>The default is to produce links to online content</value>
        public MSHelpViewerSdkLinkType MSHelpViewerSdkLinkType { get; private set; }

        /// <summary>
        /// This read-only property is used to get the Product ID portion of the MS Help Viewer 1.0 Catalog ID
        /// </summary>
        /// <value>If not specified, the default is "VS".</value>
        /// <remarks><para>The MS Help Viewer Catalog 1.0 ID is composed of the <c>CatalogProductId</c> the
        /// <see cref="CatalogVersion"/>, and the <see cref="Language"/> code. For example, the English Visual
        /// Studio 10 catalog is <c>VS_100_EN-US</c>.</para>
        /// 
        /// <note type="note">You should typically use the default value</note>
        /// </remarks>
        [EscapeValue]
        public string CatalogProductId
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "VS";
                else
                    value = Uri.EscapeDataString(Uri.UnescapeDataString(value.Trim()));

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the Version portion of the MS Help Viewer 1.0 Catalog ID
        /// </summary>
        /// <value>If not specified, the default is "100"</value>
        /// <remarks><para>The MS Help Viewer 1.0 Catalog ID is composed of the <see cref="CatalogProductId"/>,
        /// the <c>CatalogVersion</c>, and the <see cref="Language"/> code. For example, the English Visual
        /// Studio 10 catalog is <c>VS_100_EN-US</c>.</para>
        /// 
        /// <note type="note">You should typically used the default value</note>
        /// </remarks>
        [EscapeValue]
        public string CatalogVersion
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "100";
                else
                    value = value.Trim();

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get a non-standard MS Help Viewer 2.x content catalog name
        /// </summary>
        /// <value>If not specified, the default will be set based on the Visual Studio version catalog related
        /// to the Help Viewer (VisualStudio12 for Visual Studio 2013 for example).</value>
        [EscapeValue]
        public string CatalogName
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = String.Empty;
                else
                    value = Uri.EscapeDataString(Uri.UnescapeDataString(value.Trim()));

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the vendor name for the help viewer file
        /// </summary>
        /// <value>The default if not specified will be "Vendor Name".  The value must not contain the ':',
        /// '\', '/', '.', ',', '#', or '&amp;' characters.</value>
        [EscapeValue]
        public string VendorName
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = String.Empty;
                else
                {
                    // Replace any problem characters in their normal and/or encoded forms
                    value = reBadVendorNameChars.Replace(Uri.UnescapeDataString(value.Trim()), String.Empty);
                }

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the product title for the help viewer file
        /// </summary>
        /// <value>The default if not specified will be the value of the <see cref="HelpTitle" /> property</value>
        [EscapeValue]
        public string ProductTitle
        {
            get => field;
            private set => field = (value ?? String.Empty).Trim();
        }

        /// <summary>
        /// This read-only property is used to get the topic version for each topic in the help file
        /// </summary>
        /// <value>The default is "100" (meaning 10.0)</value>
        [EscapeValue]
        public string TopicVersion
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "100";
                else
                    value = value.Trim();

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the table of contents parent for each root topic in the help
        /// file.
        /// </summary>
        /// <value>The default is "-1" to show the root topics in the root of the main table of content</value>
        [EscapeValue]
        public string TocParentId
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "-1";
                else
                    value = value.Trim();

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the topic version of the <see cref="TocParentId" /> topic
        /// </summary>
        /// <value>The default is "100" meaning "10.0"</value>
        [EscapeValue]
        public string TocParentVersion
        {
            get => field;
            private set
            {
                if(String.IsNullOrWhiteSpace(value))
                    value = "100";
                else
                    value = value.Trim();

                field = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the sort order for conceptual content so that it appears
        /// within its parent in the correct position.
        /// </summary>
        /// <value>The default is -1 to let the build engine determine the best value to use based on the
        /// other project properties.</value>
        public int TocOrder
        {
            get => field;
            private set
            {
                if(value < -1)
                    value = -1;

                field = value;
            }
        }

        /// <summary>
        /// This is used to get or set the display version shown below entries in the search results pane in the
        /// help viewer application.
        /// </summary>
        /// <value>If not set, a display version will not be shown for topics in the search results pane</value>
        /// <remarks>If set, this typically refers to the SDK name and version or the module in which the member
        /// resides to help differentiate it from other entries with the same title in the search results.</remarks>
        public string SearchResultsDisplayVersion { get; set; }

        #endregion

        #region Website properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get the type of links used to reference other help topics
        /// referring to framework (SDK) help topics in HTML Help 1 help files.
        /// </summary>
        /// <value>The default is to produce links to online content</value>
        public HtmlSdkLinkType WebsiteSdkLinkType { get; private set; }

        /// <summary>
        /// This read-only property is used to get the ad content to place in each page in the website help file
        /// format.
        /// </summary>
        [EscapeValue]
        public string WebsiteAdContent { get; private set; }

        #endregion

        #region Markdown properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get whether or not to append ".md" extensions to topic URLs
        /// </summary>
        /// <value>The default is to false to leave them off.  This is suitable for GitHib wiki content which
        /// does not add the filename extensions.  Adding them causes the wiki to link to the raw file content
        /// rather than the rendered topic.  If your site uses them or if you are rendering content to store in
        /// source control where they are used, set this property to true.</value>
        public bool AppendMarkdownFileExtensionsToUrls { get; private set; }

        #endregion

        #region Show Missing Tags properties
        //=====================================================================

        /// <summary>
        /// This read-only helper property returns the flags to use when looking for missing tags
        /// </summary>
        public MissingTags MissingTags { get; private set; }

        /// <summary>
        /// This read-only property is used to get whether or not missing namespace comments are indicated in the
        /// help file.
        /// </summary>
        public bool ShowMissingNamespaces => (this.MissingTags & MissingTags.Namespace) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not missing &lt;summary&gt; tags are indicated in
        /// the help file.
        /// </summary>
        public bool ShowMissingSummaries => (this.MissingTags & MissingTags.Summary) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not missing &lt;param&gt; tags are indicated in the
        /// help file
        /// </summary>
        public bool ShowMissingParams => (this.MissingTags & MissingTags.Parameter) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not missing &lt;typeparam&gt; tags on generic types
        /// and methods are indicated in the help file.
        /// </summary>
        public bool ShowMissingTypeParams => (this.MissingTags & MissingTags.TypeParameter) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not missing &lt;returns&gt; tags are indicated in
        /// the help file.
        /// </summary>
        public bool ShowMissingReturns => (this.MissingTags & MissingTags.Returns) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not missing &lt;value&gt; tags are indicated in the
        /// help file.
        /// </summary>
        public bool ShowMissingValues => (this.MissingTags & MissingTags.Value) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not missing &lt;remarks&gt; tags are indicated in
        /// the help file.
        /// </summary>
        public bool ShowMissingRemarks => (this.MissingTags & MissingTags.Remarks) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not constructors are automatically documented if
        /// they are missing the &lt;summary&gt; tag and for classes with compiler generated constructors.
        /// </summary>
        public bool AutoDocumentConstructors => (this.MissingTags & MissingTags.AutoDocumentCtors) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not dispose methods are automatically documented if
        /// they are missing the &lt;summary&gt; tag and for classes with compiler generated dispose methods.
        /// </summary>
        public bool AutoDocumentDisposeMethods => (this.MissingTags & MissingTags.AutoDocumentDispose) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not missing &lt;include&gt; tag target
        /// documentation is indicated in the help file.
        /// </summary>
        public bool ShowMissingIncludeTargets => (this.MissingTags & MissingTags.IncludeTargets) != 0;

        #endregion

        #region Visibility properties
        //=====================================================================

        /// <summary>
        /// This read-only helper property returns the flags used to indicate which optional items to document
        /// </summary>
        public VisibleItems VisibleItems { get; private set; }

        /// <summary>
        /// This read-only property is used to get whether or not attributes on types and members are documented
        /// in the syntax portion of the help file.
        /// </summary>
        public bool DocumentAttributes => (this.VisibleItems & VisibleItems.Attributes) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not explicit interface implementations are
        /// documented.
        /// </summary>
        public bool DocumentExplicitInterfaceImplementations => (this.VisibleItems & VisibleItems.ExplicitInterfaceImplementations) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not inherited members are documented
        /// </summary>
        public bool DocumentInheritedMembers => (this.VisibleItems & VisibleItems.InheritedMembers) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not inherited framework members are documented
        /// </summary>
        public bool DocumentInheritedFrameworkMembers => (this.VisibleItems & VisibleItems.InheritedFrameworkMembers) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not inherited internal framework members are
        /// documented.
        /// </summary>
        public bool DocumentInheritedFrameworkInternalMembers => (this.VisibleItems & VisibleItems.InheritedFrameworkInternalMembers) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not inherited private framework members are
        /// documented.
        /// </summary>
        public bool DocumentInheritedFrameworkPrivateMembers => (this.VisibleItems & VisibleItems.InheritedFrameworkPrivateMembers) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not internal members are documented in the help
        /// file.
        /// </summary>
        public bool DocumentInternals => (this.VisibleItems & VisibleItems.Internals) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not private members are documented in the help file
        /// </summary>
        public bool DocumentPrivates => (this.VisibleItems & VisibleItems.Privates) != 0;

        /// <summary>
        /// This read-only property is used to get or set whether or not private fields are documented in the
        /// help file.
        /// </summary>
        public bool DocumentPrivateFields => (this.VisibleItems & VisibleItems.PrivateFields) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not protected members are documented in the help
        /// file.
        /// </summary>
        public bool DocumentProtected => (this.VisibleItems & VisibleItems.Protected) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not protected members of sealed classes are
        /// documented in the help file.
        /// </summary>
        public bool DocumentSealedProtected => (this.VisibleItems & VisibleItems.SealedProtected) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not "protected internal" members are documented as
        /// "protected" only in the help file.
        /// </summary>
        public bool DocumentProtectedInternalAsProtected => (this.VisibleItems & VisibleItems.ProtectedInternalAsProtected) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not no-PIA (Primary Interop Assembly) embedded
        /// interop types are documented in the help file.
        /// </summary>
        public bool DocumentNoPIATypes => (this.VisibleItems & VisibleItems.NoPIATypes) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not public compiler generated types and members are
        /// documented in the help file.
        /// </summary>
        public bool DocumentPublicCompilerGenerated => (this.VisibleItems & VisibleItems.PublicCompilerGenerated) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not members marked with an
        /// <see cref="EditorBrowsableAttribute"/> set to <c>Never</c> are documented.
        /// </summary>
        public bool DocumentEditorBrowsableNever => (this.VisibleItems & VisibleItems.EditorBrowsableNever) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not members marked with a
        /// <see cref="BrowsableAttribute"/> set to <c>False</c> are documented.
        /// </summary>
        public bool DocumentNonBrowsable => (this.VisibleItems & VisibleItems.NonBrowsable) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not internal members of base types in other
        /// assemblies and private members in base types are documented.
        /// </summary>
        public bool DocumentInternalAndPrivateIfExternal => (this.VisibleItems & VisibleItems.InternalAndPrivateIfExternal) != 0;

        /// <summary>
        /// This read-only property is used to get whether or not extension methods are included in member list
        /// topics.
        /// </summary>
        /// <value>Note that the property is the inverse of the underlying <see cref="VisibleItems.OmitExtensionMethods"/>
        /// value to maintain backward compatibility with prior releases.</value>
        public bool IncludeExtensionMethodsInMemberLists => (this.VisibleItems & VisibleItems.OmitExtensionMethods) == 0;

        /// <summary>
        /// This read-only property is used to get whether or not extension methods that extend <see cref="Object"/>
        /// are included in member list topics.
        /// </summary>
        /// <remarks>This has no effect if <see cref="IncludeExtensionMethodsInMemberLists"/> is false.</remarks>
        /// <value>Note that the property is the inverse of the underlying <see cref="VisibleItems.OmitObjectExtensionMethods"/>
        /// value to maintain backward compatibility with prior releases.</value>
        public bool IncludeObjectExtensionMethodsInMemberLists => (this.VisibleItems & VisibleItems.OmitObjectExtensionMethods) == 0;


        /// <summary>
        /// This read-only property is used to get the API filter
        /// </summary>
        public ApiFilterCollection ApiFilter
        {
            get
            {
                if(apiFilter == null)
                {
                    apiFilter = [];

                    var apiFilterProperty = msBuildProject.GetProperty("ApiFilter");

                    if(apiFilterProperty != null && !String.IsNullOrWhiteSpace(apiFilterProperty.UnevaluatedValue))
                        apiFilter.FromXml(apiFilterProperty.UnevaluatedValue);
                }

                return apiFilter;
            }
        }
        #endregion

        #region IBasePathProvider Members
        //=====================================================================

        /// <inheritdoc />
        public string BasePath => Path.GetDirectoryName(MSBuildProject.FullPath);

        /// <summary>
        /// This method resolves any MSBuild environment variables in the path objects
        /// </summary>
        /// <param name="path">The path to use</param>
        /// <returns>A copy of the path after performing any custom resolutions</returns>
        public string ResolvePath(string path)
        {
            return reMSBuildVar.Replace(path, buildVarMatchEval);
        }
        #endregion

        #region IContentFileProvider Members
        //=====================================================================

        /// <inheritdoc />
        public IEnumerable<ContentFile> ContentFiles(BuildAction buildAction)
        {
            ContentFile contentFile;
            string metadata;

            foreach(ProjectItem item in msBuildProject.GetItems(buildAction.ToString()))
            {
                contentFile = new ContentFile(new FilePath(item.EvaluatedInclude, this)) { ContentFileProvider = this };

                metadata = item.GetMetadataValue(BuildItemMetadata.LinkPath);

                if(!String.IsNullOrWhiteSpace(metadata))
                    contentFile.LinkPath = new FilePath(metadata, this);

                metadata = item.GetMetadataValue(BuildItemMetadata.SortOrder);

                if(!String.IsNullOrWhiteSpace(metadata) && Int32.TryParse(metadata, out int sortOrder))
                    contentFile.SortOrder = sortOrder;

                yield return contentFile;
            }
        }
        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Private constructor
        /// </summary>
        private SandcastleProject()
        {
            characterMatchEval = new MatchEvaluator(this.OnCharacterMatch);
            buildVarMatchEval = new MatchEvaluator(this.OnBuildVarMatch);

            removeProjectWhenDisposed = true;

            this.ContentPlacement = ContentPlacement.AboveNamespaces;
            this.CleanIntermediates = this.KeepLogFile = this.BinaryTOC = true;

            this.BuildLogFile = null;

            this.MissingTags = MissingTags.Summary | MissingTags.Parameter | MissingTags.TypeParameter |
                MissingTags.Returns | MissingTags.AutoDocumentCtors | MissingTags.Namespace |
                MissingTags.AutoDocumentDispose;

            this.VisibleItems = VisibleItems.InheritedFrameworkMembers | VisibleItems.InheritedMembers |
                VisibleItems.Protected | VisibleItems.ProtectedInternalAsProtected | VisibleItems.NonBrowsable;

            this.BuildAssemblerVerbosity = BuildAssemblerVerbosity.OnlyWarningsAndErrors;
            this.SaveComponentCacheCapacity = 100;
            this.HtmlSdkLinkType = this.WebsiteSdkLinkType = HtmlSdkLinkType.Msdn;
            this.MSHelpViewerSdkLinkType = MSHelpViewerSdkLinkType.Msdn;
            this.SdkLinkTarget = SdkLinkTarget.Blank;
            this.PresentationStyle = Constants.DefaultPresentationStyle;
            this.SyntaxFilters = ComponentUtilities.DefaultSyntaxFilter;
            this.HelpFileVersion = "1.0.0.0";
            this.TocOrder = -1;
            this.MaximumGroupParts = 2;

            this.OutputPath = null;
            this.HtmlHelp1xCompilerPath = this.WorkingPath = this.ComponentPath = this.SourceCodeBasePath = null;

            this.HelpTitle = this.HtmlHelpName = this.CopyrightHref = this.CopyrightText =
                this.FeedbackEMailAddress = this.FeedbackEMailLinkText = this.HeaderText = this.FooterText =
                this.ProjectSummary = this.RootNamespaceTitle = this.TopicVersion = this.TocParentId =
                this.TocParentVersion = this.CatalogProductId = this.CatalogVersion = this.CatalogName =
                this.FrameworkVersion = null;

            this.Language = null;
        }

        /// <summary>
        /// Load a Sandcastle Builder project from the given filename
        /// </summary>
        /// <param name="filename">The filename to load</param>
        /// <param name="mustExist">Specify true if the file must exist or false if a new project should be
        /// created if the file does not exist.</param>
        /// <param name="useFinalValues">True to use final evaluated property values, or false to use the
        /// unevaluated property values.  For builds, this should always be true.  If loading the project for
        /// editing, it should always be false.</param>
        /// <exception cref="ArgumentException">This is thrown if a filename is not specified or if it does not
        /// exist and <c>mustExist</c> is true.</exception>
        /// <overloads>There are three overloads for the constructor</overloads>
        public SandcastleProject(string filename, bool mustExist, bool useFinalValues) : this()
        {
            string template;

            if(String.IsNullOrEmpty(filename))
                throw new ArgumentException("A filename must be specified", nameof(filename));

            filename = Path.GetFullPath(filename);

            if(!File.Exists(filename))
            {
                if(mustExist)
                    throw new ArgumentException("The specific file must exist", nameof(filename));

                // Create new project from template file
                template = Properties.Resources.ProjectTemplate;
                template = template.Replace("$guid1$", Guid.NewGuid().ToString("B"));
                template = template.Replace("$safeprojectname$", "Documentation");

                using(StringReader sr = new(template))
                using(XmlReader xr = XmlReader.Create(sr))
                {
                    msBuildProject = new Project(xr);
                }

                msBuildProject.FullPath = filename;
            }
            else
            {
                // If already loaded into the global project collection, use the existing instance
                var loadedProjects = ProjectCollection.GlobalProjectCollection.GetLoadedProjects(filename).ToList();

                if(loadedProjects.Count != 0)
                {
                    msBuildProject = loadedProjects[0];
                    removeProjectWhenDisposed = false;
                }
                else
                    msBuildProject = new Project(filename);
            }

            this.UsingFinalValues = useFinalValues;
            this.LoadProperties();
        }

        /// <summary>
        /// This is used to create a Sandcastle Builder project from an existing MSBuild project instance
        /// </summary>
        /// <param name="existingProject">The existing project instance</param>
        /// <remarks>It is assumed that the project has been loaded, the property values are current, and that
        /// the configuration and platform have been set in the MSBuild project global properties in order to
        /// get the correct final values.</remarks>
        public SandcastleProject(Project existingProject) : this()
        {
            // Do not remove the project from the MSBuild project collection when this is disposed of since we
            // didn't create it.
            removeProjectWhenDisposed = false;

            msBuildProject = existingProject;
            msBuildProject.ReevaluateIfNecessary();

            this.UsingFinalValues = true;
            this.LoadProperties();
        }
        #endregion

        #region IDisposable implementation
        //=====================================================================

        /// <summary>
        /// This handles garbage collection to ensure proper disposal of the Sandcastle project if not done
        /// explicitly with <see cref="Dispose()"/>.
        /// </summary>
        ~SandcastleProject()
        {
            this.Dispose();
        }

        /// <summary>
        /// This properly disposes of the Sandcastle project
        /// </summary>
        public void Dispose()
        {
            // If we loaded the MSBuild project, we must unload it.  If not, it is cached and will cause problems
            // if loaded a second time.
            if(removeProjectWhenDisposed && msBuildProject != null && !String.IsNullOrEmpty(this.Filename))
            {
                ProjectCollection.GlobalProjectCollection.UnloadProject(msBuildProject);
                ProjectCollection.GlobalProjectCollection.UnloadProject(msBuildProject.Xml);
            }

            GC.SuppressFinalize(this);
        }
        #endregion

        #region Private methods
        //=====================================================================

        /// <summary>
        /// This is used to initialize the local property info and property descriptor caches
        /// </summary>
        private static Dictionary<string, PropertyInfo> InitializePropertyCache()
        {
            PropertyInfo[] propertyInfo;

            propertyCache = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            pdcCache = TypeDescriptor.GetProperties(typeof(SandcastleProject));

            propertyInfo = typeof(SandcastleProject).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach(PropertyInfo property in propertyInfo)
                propertyCache.Add(property.Name, property);

            return propertyCache;
        }

        /// <summary>
        /// Replace a \xNN value in the copyright text with its actual character
        /// </summary>
        /// <param name="match">The match that was found</param>
        /// <returns>The string to use as the replacement</returns>
        private string OnCharacterMatch(Match match)
        {
            // Ignore it if it is escaped
            if(match.Index != 0 && this.CopyrightText[match.Index - 1] == '\\')
                return match.Value;

            int value = Convert.ToInt32(match.Value.Substring(2), 16);
            char ch = (char)value;

            return new String(ch, 1);
        }

        /// <summary>
        /// Resolve references to MSBuild variables in a path value
        /// </summary>
        /// <param name="match">The match that was found</param>
        /// <returns>The string to use as the replacement</returns>
        private string OnBuildVarMatch(Match match)
        {
            if(!this.ProjectPropertyCache.TryGetValue(match.Groups[1].Value, out ProjectProperty prop))
                return String.Empty;

            return prop.EvaluatedValue;
        }

        /// <summary>
        /// This is used to load the properties from the project file
        /// </summary>
        private void LoadProperties()
        {
            Version schemaVersion;
            string helpFormats;
            Dictionary<string, string> translateFormat = new()
            {
                { "HTMLHELP1X", "HtmlHelp1" },
                { "HTMLHELP2X", "HtmlHelp1" },
                { "HELP1XANDHELP2X", "HtmlHelp1" },
                { "HELP1XANDWEBSITE", "HtmlHelp1, Website" },
                { "HELP2XANDWEBSITE", "Website" },
                { "HELP1XAND2XANDWEBSITE", "HtmlHelp1, Website" },
                { "MSHELP2", "HtmlHelp1" }
            };

            try
            {
                // Ensure that we use the correct build engine for the project.  Version 14.0 or later is required.
                if(!Double.TryParse(msBuildProject.Xml.ToolsVersion, out double toolsVersion))
                    toolsVersion = 0.0;

                if(toolsVersion < 14.0)
                    msBuildProject.Xml.ToolsVersion = "14.0";

                if(!this.ProjectPropertyCache.TryGetValue("SHFBSchemaVersion", out ProjectProperty property))
                    throw new BuilderException("PRJ0001", "Invalid or missing SHFBSchemaVersion");

                if(String.IsNullOrEmpty(property.EvaluatedValue))
                    throw new BuilderException("PRJ0001", "Invalid or missing SHFBSchemaVersion");

                schemaVersion = new Version(property.EvaluatedValue);

                if(schemaVersion > SchemaVersion)
                    throw new BuilderException("PRJ0002", "The selected file is for a more recent version of " +
                        "the help file builder.  Please upgrade your copy to load the file.");

                // Note that many properties don't use the final value as they don't contain variables that
                // need replacing.
                foreach(ProjectProperty prop in this.ProjectPropertyCache.Values)
                {
                    switch(prop.Name.ToLowerInvariant())
                    {
                        case "apifilter":           // These collections are created as needed
                        case "documentationsources":
                        case "namespacesummaries":
                        case "componentconfigurations":
                        case "pluginconfigurations":
                        case "configuration":       // These properties are ignored
                        case "platform":
                        case "transformcomponentarguments":
                            break;

                        case "helpfileformat":
                            // The enum value names changed in v1.8.0.3
                            if(schemaVersion.Major == 1 && schemaVersion.Minor == 8 &&
                              schemaVersion.Build == 0 && schemaVersion.Revision < 3)
                            {
                                helpFormats = prop.UnevaluatedValue.ToUpper(CultureInfo.InvariantCulture);

                                foreach(string key in translateFormat.Keys)
                                    helpFormats = helpFormats.Replace(key, translateFormat[key]);

                                this.SetLocalProperty(prop, helpFormats);

                                msBuildProject.SetProperty("HelpFileFormat", this.HelpFileFormat.ToString());
                            }
                            else
                                if(schemaVersion.Major < 2015 && prop.UnevaluatedValue.Contains("MSHelp2"))
                            {
                                // Help 2 was last supported in the 2015.5.2.0 release
                                helpFormats = prop.UnevaluatedValue.Replace("MSHelp2", "HtmlHelp1");

                                this.SetLocalProperty(prop, helpFormats);

                                msBuildProject.SetProperty("HelpFileFormat", this.HelpFileFormat.ToString());
                            }
                            else
                                this.SetLocalProperty(prop, prop.UnevaluatedValue);
                            break;

                        case "frameworkversion":
                            // The values changed in v1.9.2.0 to include the framework type.  They changed in
                            // v1.9.5.0 to use the titles from the Sandcastle framework definition file.
                            if(schemaVersion.Major == 1 && (schemaVersion.Minor < 9 ||
                              (schemaVersion.Minor == 9 && schemaVersion.Build < 5)))
                            {
                                this.SetLocalProperty(prop, ConvertOldFrameworkVersion(prop.UnevaluatedValue));
                                msBuildProject.SetProperty("FrameworkVersion", this.FrameworkVersion);
                            }
                            else
                                this.SetLocalProperty(prop, prop.UnevaluatedValue);
                            break;

                        case "presentationstyle":
                            // Convert removed presentation styles to the current default presentation style
                            switch(prop.UnevaluatedValue)
                            {
                                case "Hana":
                                case "Prototype":
                                case "VS2005":
                                    this.SetLocalProperty(prop, Constants.DefaultPresentationStyle);
                                    msBuildProject.SetProperty("PresentationStyle", Constants.DefaultPresentationStyle);
                                    break;

                                default:
                                    this.SetLocalProperty(prop, prop.UnevaluatedValue);
                                    break;
                            }
                            break;

                        case "visibleitems":
                            this.SetLocalProperty(prop, prop.UnevaluatedValue);

                            // Editor Browsable and Non-Browsable were added in v2017.9.26.0.  Turn them on by
                            // default in older projects to keep the same behavior until turned off explicitly.
                            if(schemaVersion < new Version(2017, 9, 26, 0))
                            {
                                this.VisibleItems |= (VisibleItems.EditorBrowsableNever | VisibleItems.NonBrowsable);
                                msBuildProject.SetProperty("VisibleItems", this.VisibleItems.ToString());
                            }
                            break;

                        default:
                            // These may or may not contain variable references so use the final value if requested
                            if(UsingFinalValues)
                                this.SetLocalProperty(prop, prop.EvaluatedValue);
                            else
                            {
                                // A recent change to Microsoft.Common.targets overrides our OutputPath property
                                // with a copy containing a function call that ensures a trailing backslash as the
                                // unevaluated value.  If we see an imported property with a predecessor, use the
                                // predecessor to get our unevaluated value.
                                var p = prop;

                                while(p.IsImported && p.Predecessor != null)
                                    p = p.Predecessor;

                                this.SetLocalProperty(p, p.UnevaluatedValue);
                            }
                            break;
                    }
                }

                // Note: Project data stored in item groups are loaded on demand when first used (i.e.
                // references, additional and conceptual content, etc.)
            }
            catch(Exception ex)
            {
                throw new BuilderException("PRJ0003", String.Format(CultureInfo.CurrentCulture,
                    "Error reading project from '{0}':\r\n{1}", msBuildProject.FullPath, ex.Message), ex);
            }
        }

        /// <summary>
        /// This is used to set the local property to the specified value of the MSBuild project property using
        /// Reflection.
        /// </summary>
        /// <param name="msBuildProperty">The MSBuild project property to use</param>
        /// <param name="value">The value to which the local property is set</param>
        /// <remarks>Property name matching is case insensitive as are the values.  This is used to allow setting
        /// of simple project properties (non-collection) from the MSBuild project file.  Unknown properties are
        /// ignored.</remarks>
        /// <exception cref="ArgumentNullException">This is thrown if the name parameter is null or an empty
        /// string.</exception>
        /// <exception cref="BuilderException">This is thrown if an error occurs while trying to set the named
        /// property.</exception>
        private void SetLocalProperty(ProjectProperty msBuildProperty, string value)
        {
            TypeConverter tc;
            EscapeValueAttribute escAttr;
            FilePath filePath;
            object parsedValue;

            if(msBuildProperty == null)
                throw new ArgumentNullException(nameof(msBuildProperty));

            // Ignore unknown properties
            if(!propertyCache.TryGetValue(msBuildProperty.Name, out PropertyInfo localProperty))
                return;

            // This can happen on rare occasions, usually on build servers.  Typically, the property in question
            // is not related to the SHFB project itself.  The name just happens to match a restricted property.
            // It's better to ignore it rather than abort loading the project and break the build.  We'll just
            // log it to the output window for reference when debugging.
            if(!localProperty.CanWrite || localProperty.IsDefined(typeof(XmlIgnoreAttribute), true))
            {
                System.Diagnostics.Debug.WriteLine("**** An attempt was made to set a read-only or ignored " +
                    "property: {0}   Value: {1}", msBuildProperty.Name, value);
                return;
            }

            try
            {
                // If escaped, unescape it
                escAttr = pdcCache[localProperty.Name].Attributes[typeof(EscapeValueAttribute)] as EscapeValueAttribute;

                if(escAttr != null)
                    value = EscapeValueAttribute.Unescape(value);

                if(localProperty.PropertyType.IsEnum)
                    parsedValue = Enum.Parse(localProperty.PropertyType, value, true);
                else
                    if(localProperty.PropertyType == typeof(Version))
                        parsedValue = new Version(value);
                    else
                    {
                        if(localProperty.PropertyType == typeof(FilePath))
                            parsedValue = new FilePath(value, this);
                        else
                            if(localProperty.PropertyType == typeof(FolderPath))
                                parsedValue = new FolderPath(value, this);
                            else
                            {
                                tc = TypeDescriptor.GetConverter(localProperty.PropertyType);
                                parsedValue = tc.ConvertFromString(value);
                            }

                        // If it's a file or folder path, set the IsFixedPath property based on whether or not
                        // it is rooted.
                        filePath = parsedValue as FilePath;

                        if(filePath != null && Path.IsPathRooted(value))
                            filePath.IsFixedPath = true;
                    }
            }
            catch(Exception ex)
            {
                // Ignore exceptions for environment variable properties.  A few people have had an environment
                // variable with a SHFB project property name that gets picked up as a default and the value
                // isn't typically valid for a SHFB project property (i.e. Language which expects a valid culture
                // name).  Fail on any others though.
                if(!msBuildProperty.IsEnvironmentProperty)
                    throw new BuilderException("PRJ0005", "Unable to parse value '" + value +
                        "' for property '" + msBuildProperty.Name + "'", ex);

                parsedValue = null;
            }

            localProperty.SetValue(this, parsedValue, null);
        }

        /// <summary>
        /// This is used to convert old SHFB project framework version values to the new framework version values
        /// </summary>
        /// <param name="oldValue">The old value to convert</param>
        /// <returns>The equivalent new value</returns>
        private static string ConvertOldFrameworkVersion(string oldValue)
        {
            if(String.IsNullOrWhiteSpace(oldValue))
                return ReflectionDataSetDictionary.DefaultFrameworkTitle;

            oldValue = oldValue.Trim();

            if(oldValue.IndexOf(".NET ", StringComparison.OrdinalIgnoreCase) != -1 || Char.IsDigit(oldValue[0]))
            {
                oldValue = oldValue.ToUpperInvariant().Replace(".NET ", String.Empty).Trim();

                if(oldValue.Length == 0)
                    oldValue = "4.0";
                else
                    if(oldValue.Length > 3)
                        oldValue = oldValue.Substring(0, 3);

                oldValue = ".NET Framework " + oldValue;
            }
            else
                if(oldValue.IndexOf("Silverlight ", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    oldValue = oldValue.ToUpperInvariant().Trim();

                    if(oldValue.EndsWith(".0", StringComparison.Ordinal))
                        oldValue = oldValue.Substring(0, oldValue.Length - 2);
                }
                else
                    if(oldValue.IndexOf("Portable ", StringComparison.OrdinalIgnoreCase) != -1)
                        oldValue = ".NET Portable Library 4.0 (Legacy)";

            var rdsd = new ReflectionDataSetDictionary(null);

            // If not found, use the default
            if(!rdsd.TryGetValue(oldValue, out ReflectionDataSet dataSet))
                return ReflectionDataSetDictionary.DefaultFrameworkTitle;

            return dataSet.Title;
        }

        /// <summary>
        /// This is used to set the default help file format when one isn't explicitly defined in the project
        /// </summary>
        /// <param name="defaultFormat">The default help file format to use</param>
        internal void SetDefaultHelpFileFormat(HelpFileFormats defaultFormat)
        {
            this.HelpFileFormat = defaultFormat;
        }
        #endregion

        #region Public methods
        //=====================================================================

        /// <summary>
        /// This is used to determine the default build action for a file based on its extension
        /// </summary>
        /// <param name="filename">The filename to use</param>
        /// <returns>The build action based on the extension</returns>
        public static BuildAction DefaultBuildAction(string filename)
        {
            string ext = Path.GetExtension(filename).ToLowerInvariant();

            switch(ext)
            {
                case ".asp":        // Content/configuration
                case ".aspx":
                case ".ascx":
                case ".config":
                case ".css":
                case ".htm":
                case ".html":
                case ".js":
                case ".md":
                case ".txt":
                case ".zip":
                    return BuildAction.Content;

                case ".bmp":        // Images for conceptual content.  The default used to be Content but
                case ".gif":        // the additional content model has been deprecated so this is now the
                case ".jpg":        // more appropriate choice.
                case ".jpeg":
                case ".png":
                    return BuildAction.Image;

                case ".content":
                    return BuildAction.ContentLayout;

                case ".items":
                    return BuildAction.ResourceItems;

                case ".sitemap":
                    return BuildAction.SiteMap;

                case ".snippets":
                    return BuildAction.CodeSnippets;

                case ".tokens":
                    return BuildAction.Tokens;

                case ".xamlcfg":
                    return BuildAction.XamlConfiguration;

                default:    // Anything else defaults to None
                    break;
            }

            return BuildAction.None;
        }

        /// <summary>
        /// This is used to determine whether or not the given name can be used for a user-defined project
        /// property.
        /// </summary>
        /// <param name="name">The name to check</param>
        /// <returns>True if it can be used, false if it cannot be used</returns>
        public bool IsValidUserDefinedPropertyName(string name)
        {
            if(msBuildProject == null || propertyCache.ContainsKey(name) || RestrictedProperties.Contains(name))
                return false;

            if(this.ProjectPropertyCache.TryGetValue(name, out ProjectProperty prop))
                return (!prop.IsImported && !prop.IsReservedProperty);

            return true;
        }

        /// <summary>
        /// Add a new folder build item to the project
        /// </summary>
        /// <param name="folder">The folder name</param>
        /// <returns>The new <see cref="FileItem"/>.</returns>
        /// <remarks>If the folder does not exist in the project, it is added and created if not already there.
        /// If the folder is already part of the project, the existing item is returned.</remarks>
        /// <exception cref="ArgumentException">This is thrown if the path matches the project root path or is
        /// not below it.</exception>
        public IFileItem AddFolderToProject(string folder)
        {
            FolderPath folderPath;
            FileItem newFileItem = null;
            string folderAction = BuildAction.Folder.ToString(),
                rootPath = Path.GetDirectoryName(msBuildProject.FullPath);

            if(folder == null)
                throw new ArgumentNullException(nameof(folder));

            if(folder.Length != 0 && folder[folder.Length - 1] == Path.DirectorySeparatorChar)
                folder = folder.Substring(0, folder.Length - 1);

            if(!Path.IsPathRooted(folder))
                folder = Path.GetFullPath(Path.Combine(rootPath, folder));

            if(String.Compare(folder, 0, rootPath, 0, rootPath.Length, StringComparison.OrdinalIgnoreCase) != 0)
                throw new ArgumentException("The folder must be below the project's root path", nameof(folder));

            if(folder.Length == rootPath.Length)
                throw new ArgumentException("The folder cannot match the project's root path", nameof(folder));

            folderPath = new FolderPath(folder, this);

            // Note that Visual Studio doesn't add the trailing backslash so look for a match with and without it.
            // Folders don't always have a relative path in the item when first added.  As such, check both the
            // relative and full paths for a match.
            foreach(ProjectItem item in msBuildProject.GetItems(folderAction))
            {
                if(item.EvaluatedInclude == folderPath.PersistablePath ||
                  item.EvaluatedInclude + Path.DirectorySeparatorChar == folderPath.PersistablePath ||
                  item.EvaluatedInclude == folderPath.Path ||
                  item.EvaluatedInclude + Path.DirectorySeparatorChar == folderPath.Path)
                {
                    newFileItem = new FileItem(this, item);
                    break;
                }
            }

            if(!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            if(newFileItem == null)
                newFileItem = new FileItem(this, folderAction, folder);

            return newFileItem;
        }

        /// <summary>
        /// Add a new file build item to the project
        /// </summary>
        /// <param name="sourceFile">The source filename</param>
        /// <param name="destinationFile">The optional destination path.  If empty, null, or it does not start with the
        /// project folder, the file is copied to the root folder of the project.</param>
        /// <returns>The new <see cref="FileItem" /></returns>
        /// <remarks>If the file does not exist in the project, it is copied to the destination path or project
        /// folder if not already there.  The default build action is determined based on the filename's
        /// extension.  If the file is already part of the project, the existing item is returned.</remarks>
        public IFileItem AddFileToProject(string sourceFile, string destinationFile)
        {
            BuildAction buildAction;
            FilePath filePath;
            FileItem newFileItem = null;
            string[] folders;
            string itemPath, rootPath = Path.GetDirectoryName(msBuildProject.FullPath);

            if(String.IsNullOrEmpty(destinationFile) || !destinationFile.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                destinationFile = Path.Combine(rootPath, Path.GetFileName(sourceFile));

            filePath = new FilePath(destinationFile, this);
            buildAction = DefaultBuildAction(destinationFile);

            foreach(ProjectItem item in msBuildProject.AllEvaluatedItems)
            {
                if(item.HasMetadata(BuildItemMetadata.LinkPath))
                    itemPath = item.GetMetadataValue(BuildItemMetadata.LinkPath);
                else
                    itemPath = item.EvaluatedInclude;

                // Files don't always have a relative path in the item when first added.  As such, check both
                // the relative and full paths for a match.
                if(itemPath == filePath.PersistablePath || itemPath == filePath.Path)
                {
                    newFileItem = new FileItem(this, item);
                    break;
                }
            }

            if(!File.Exists(destinationFile))
            {
                if(!Directory.Exists(Path.GetDirectoryName(destinationFile)))
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));

                // Make sure folder items exists for all parts of the path
                folders = Path.GetDirectoryName(filePath.PersistablePath).Split(Path.DirectorySeparatorChar);
                itemPath = String.Empty;

                foreach(string path in folders)
                {
                    if(path.Length != 0)
                    {
                        itemPath += path + Path.DirectorySeparatorChar;
                        this.AddFolderToProject(itemPath);
                    }
                }

                if(File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, destinationFile);
                    File.SetAttributes(destinationFile, FileAttributes.Normal);
                }
            }

            if(newFileItem == null)
            {
                newFileItem = new FileItem(this, buildAction.ToString(), destinationFile);

                // For images, assign the build action again so that it sets the default alternate text and ID
                if(buildAction == BuildAction.Image)
                    newFileItem.BuildAction = buildAction;
            }

            return newFileItem;
        }

        /// <summary>
        /// This is used to locate a file by name in the project
        /// </summary>
        /// <param name="fileToFind">The fully qualified file path to find</param>
        /// <returns>The file item if found or null if not found</returns>
        public IFileItem FindFile(string fileToFind)
        {
            FilePath filePath;
            FileItem fileItem = null;
            string itemPath, rootPath = Path.GetDirectoryName(msBuildProject.FullPath);

            if(String.IsNullOrEmpty(fileToFind) ||
              !fileToFind.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                return null;

            filePath = new FilePath(fileToFind, this);

            foreach(ProjectItem item in msBuildProject.AllEvaluatedItems)
            {
                if(item.HasMetadata(BuildItemMetadata.LinkPath))
                    itemPath = item.GetMetadataValue(BuildItemMetadata.LinkPath);
                else
                    itemPath = item.EvaluatedInclude;

                // Files don't always have a relative path in the item when first added.  As such, check both
                // the relative and full paths for a match.
                if(itemPath == filePath.PersistablePath || itemPath == filePath.Path)
                {
                    fileItem = new FileItem(this, item);
                    break;
                }
            }

            return fileItem;
        }

        /// <summary>
        /// This refreshes the project instance property values by reloading them from the underlying MSBuild
        /// project.
        /// </summary>
        public void RefreshProjectProperties()
        {
            projectPropertyCache = null;
            docSources = null;
            apiFilter = null;
            namespaceSummaries = null;
            componentConfigs = null;
            plugInConfigs = null;

            msBuildProject.ReevaluateIfNecessary();

            this.LoadProperties();
        }

        /// <summary>
        /// This is used to save the project file
        /// </summary>
        /// <param name="filename">The filename for the project</param>
        public void SaveProject(string filename)
        {
            try
            {
                filename = Path.GetFullPath(filename);

                msBuildProject.FullPath = filename;

                // Update the schema version if necessary but only if the project is dirty
                var property = msBuildProject.AllEvaluatedProperties.FirstOrDefault(p => p.Name == "SHFBSchemaVersion");

                if(property == null || !Version.TryParse(property.EvaluatedValue, out Version schemaVersion))
                    schemaVersion = new Version(1, 0, 0, 0);

                if(schemaVersion != SchemaVersion)
                    msBuildProject.SetProperty("SHFBSchemaVersion", SchemaVersion.ToString(4));

                msBuildProject.Save(filename);
            }
            catch(Exception ex)
            {
                throw new BuilderException("PRJ0006", String.Format(CultureInfo.CurrentCulture,
                    "Error saving project to '{0}':\r\n{1}", filename, ex.Message), ex);
            }
        }

        /// <summary>
        /// This returns true if the project contains items using the given build action
        /// </summary>
        /// <param name="buildAction">The build action for which to check</param>
        /// <returns>True if at least one item has the given build action or false if there are no items with
        /// the given build action.</returns>
        public bool HasItems(BuildAction buildAction)
        {
            return msBuildProject.GetItems(buildAction.ToString()).Count != 0;
        }

        /// <inheritdoc />
        public ISandcastleProject Clone()
        {
            return new SandcastleProject(this.MSBuildProject);
        }

        /// <inheritdoc />
        public IBuildProcess CreateBuildProcess(PartialBuildType partialBuildType)
        {
            return new BuildProcess(this, partialBuildType);
        }

        /// <summary>
        /// This is used by the replacement tag handler to get simple project property values that require no
        /// other modification or simple ones that can be handled here.
        /// </summary>
        /// <param name="name">The property name for which to get the value</param>
        /// <returns>The property value as a string if found or null if not found.  If the property name starts
        /// with "HtmlEnc", the return value is HTML encoded.  Boolean values and those ending with "SdkLinkType"
        /// are converted to lowercase for use in XML attribute values.</returns>
        public string ReplacementValueFor(string name)
        {
            bool htmlEncode = false;

            if(String.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if(name.StartsWith("HtmlEnc", StringComparison.OrdinalIgnoreCase))
            {
                htmlEncode = true;
                name = name.Substring(7);
            }

            if(!propertyCache.TryGetValue(name, out PropertyInfo property))
                return null;

            object value = property.GetValue(this);

            if(value is Boolean || name.EndsWith("SdkLinkType", StringComparison.OrdinalIgnoreCase))
                return value.ToString().ToLowerInvariant();

            if(htmlEncode)
                return WebUtility.HtmlEncode(value.ToString());

            return value.ToString();
        }
        #endregion
    }
}

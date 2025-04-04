//===============================================================================================================
// System  : Sandcastle Help File Builder
// File    : AssemblyInfoShared.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 03/22/2025
// Note    : Copyright 2006-2025, Eric Woodruff, All rights reserved
//
// Sandcastle Help File Builder common assembly attributes.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
// Version     Date     Who  Comments
// ==============================================================================================================
#region Prior history
// 1.0.0.0  08/02/2006  EFW  Created the code
// 1.1.0.0  08/22/2006  EFW  Added support for building MS Help 2 files
// 1.2.0.0  09/02/2006  EFW  Various additions and updates
// 1.3.0.0  09/08/2006  EFW  Various additions and updates
// 1.3.2.0  10/10/2006  EFW  Various additions and updates
// 1.3.4.0  12/24/2006  EFW  Various additions and updates
// 1.5.0.0  06/19/2007  EFW  Various additions and updates
// 1.6.0.0  06/19/2007  EFW  Various additions and updates
// 1.8.0.0  06/20/2008  EFW  Implemented new MSBuild project format
// 1.9.0.0  06/06/2010  EFW  Added support for generating MS Help Viewer files
// 1.9.1.0  07/09/2010  EFW  Updated for use with .NET 4.0 and MSBuild 4.0.
// 1.9.4.0  04/15/2012  EFW  Updated for use with Sandcastle 2.7.0.0
// 1.9.5.0  09/30/2012  EFW  Updated for use with Sandcastle 2.7.1.0
#endregion
// -------  12/22/2013  EFW  Updated the version numbering scheme to use a date-based value
//===============================================================================================================

using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// NOTE: See AssemblyInfo.cs for project-specific assembly attributes

// General assembly information
[assembly: AssemblyProduct("Sandcastle Help File Builder and Tools")]
[assembly: AssemblyCompany("Eric Woodruff")]
[assembly: AssemblyCopyright(AssemblyInfo.Copyright)]
[assembly: AssemblyCulture("")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif

// Not visible to COM
[assembly: ComVisible(false)]

// Resources contained within the assembly are English
[assembly: NeutralResourcesLanguage("en")]

// Version numbers.  See comments below.

// Certain assemblies may contain a specific version to maintain binary compatibility with a prior release
#if !ASSEMBLYSPECIFICVERSION
[assembly: AssemblyVersion(AssemblyInfo.StrongNameVersion)]
#endif

[assembly: AssemblyFileVersion(AssemblyInfo.FileVersion)]
[assembly: AssemblyInformationalVersion(AssemblyInfo.ProductVersion)]

// This defines constants that can be used by plug-ins and components in their metadata.
//
// All version numbers for an assembly consists of the following four values:
//
//      Year of release
//      Month of release
//      Day of release
//      Revision (typically zero unless multiple releases are made on the same day)
//
// This versioning scheme allows build component and plug-in developers to use the same major, minor, and build
// numbers as the Sandcastle tools to indicate with which version their components are compatible.
//
internal static partial class AssemblyInfo
{
    // Common assembly strong name version - DO NOT CHANGE UNLESS NECESSARY.
    //
    // This is used to set the assembly version in the strong name.  This should remain unchanged to maintain
    // binary compatibility with prior releases.  It should only be changed if a breaking change is made that
    // requires assemblies that reference older versions to be recompiled against the newer version.
    public const string StrongNameVersion = "2025.3.22.0";

    // Common assembly file version
    //
    // This is used to set the assembly file version.  This will change with each new release.  MSIs only
    // support a Major value between 0 and 255 so we drop the century from the year on this one.
    public const string FileVersion = "25.3.22.0";

    // Common product version
    //
    // This may contain additional text to indicate Alpha or Beta states.  The version number will always match
    // the file version above but includes the century on the year.
    public const string ProductVersion = "2025.3.22.0";

    // Assembly copyright information
    public const string Copyright = "Copyright \xA9 2006-2025, Eric Woodruff, All Rights Reserved";
}

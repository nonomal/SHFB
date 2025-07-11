// Copyright � Microsoft Corporation.
// This source file is subject to the Microsoft Permissive License.
// See http://www.microsoft.com/resources/sharedsource/licensingbasics/sharedsourcelicenses.mspx.
// All other rights reserved.

// Change history:
// 02/09/2012 - EFW - Updated WriteParameter() so that it notes optional parameters indicated by
// OptionalAttribute alone (no default value).
// 11/30/2012 - EFW - Added updates based on changes submitted by ComponentOne to fix crashes caused by
// obfuscated member names.
// 11/20/2013 - EFW - Cleaned up the code and removed unused members.  Added code to apply visibility settings
// to property getters and setters.  Added code to write out type data for the interop attributes that are
// converted to type metadata.
// 08/06/2014 - EFW - Added code to write out values for literal (constant) fields.
// 08/23/2016 - EFW - Added support for writing out source code context
// 03/17/2017 - EFW - Added support for value tuples
// 05/26/2017 - EFW - Fixed up issues with unsigned long enumerated types and duplicate flag values
// 05/30/2017 - JRC - Fixed issue with negative enums
// 03/14/2021 - EFW - Added support for defaultValue element for default value structs on parameters
// 03/16/2021 - EFW - Added support for writing out the nullable context for nullable reference types
// 04/03/2021 - EFW - Moved duplicate type/member merging and XAML syntax attributes to MRefBuilder and removed
// the MergeDuplicates.xsl and AddXamlSyntaxData.xsl transformations.
// 06/13/2022 - EFW - Added support for writing out hierarchy info for structures
// 09/08/2022 - EFW - Added support for init only setters
// 01/04/2024 - EFW - Added support for .NET 7 static interface members
// 03/21/2025 - EFW - Changed handling of invalid XML characters to write them out as hex values to prevent loss
// of constant values.  Added support for writing out decimal constants.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Linq;

using System.Compiler;

using Sandcastle.Tools.Reflection;

using Sandcastle.Core;
using System.Text;
using System.Diagnostics;

namespace Sandcastle.Tools
{
    /// <summary>
    /// This class is used to write out information gained from managed reflection
    /// </summary>
    public class ManagedReflectionWriter : ApiVisitor
    {
        #region Private data members
        //=====================================================================

        private readonly XmlWriter writer;
        private readonly ApiNamer namer;

        private readonly HashSet<string> assemblyNames;
        private readonly Dictionary<TypeNode, List<TypeNode>> descendantIndex;
        private readonly Dictionary<Interface, List<TypeNode>> implementorIndex;

        private readonly Dictionary<string, List<MRefBuilderCallback>> startTagCallbacks, endTagCallbacks;

        private int namespaceCount, typeCount, memberCount;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This read-only property returns the API namer being used
        /// </summary>
        public ApiNamer ApiNamer => namer;

        /// <summary>
        /// This read-only property returns a count of the namespaces found
        /// </summary>
        public int NamespaceCount => namespaceCount;

        /// <summary>
        /// This read-only property returns a count of the types found
        /// </summary>
        public int TypeCount => typeCount;

        /// <summary>
        /// This read-only property returns a count of the members found
        /// </summary>
        public int MemberCount => memberCount;

        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="output">The text writer to which the output is written</param>
        /// <param name="namer">The API namer to use</param>
        /// <param name="resolver">The assembly resolver to use</param>
        /// <param name="sourceCodeBasePath">An optional base path to the source code.  If set, source code
        /// context information will be included in the reflection data when possible.</param>
        /// <param name="warnOnMissingContext">True to report missing type source contexts as warnings rather
        /// than as informational messages.</param>
        /// <param name="filter">The API filter to use</param>
        public ManagedReflectionWriter(TextWriter output, ApiNamer namer, AssemblyResolver resolver,
          string sourceCodeBasePath, bool warnOnMissingContext, ApiFilter filter) :
          base(sourceCodeBasePath, warnOnMissingContext, resolver, filter)
        {
            assemblyNames = [];
            descendantIndex = [];
            implementorIndex = [];

            startTagCallbacks = [];
            endTagCallbacks = [];

            writer = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, CloseOutput = true });

            this.namer = namer;
        }
        #endregion

        #region Dispose and visitor method overrides
        //=====================================================================

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if(disposing && writer.WriteState != WriteState.Closed)
                writer.Close();

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        protected override void VisitNamespaces(NamespaceList spaces)
        {
            if(spaces == null)
                throw new ArgumentNullException(nameof(spaces));

            // Construct an assembly catalog
            foreach(AssemblyNode assembly in this.Assemblies)
                assemblyNames.Add(assembly.StrongName);

            // Catalog type hierarchy and interface implementors
            foreach(var ns in spaces)
            {
                foreach(var type in ns.Types)
                {
                    if(base.ApiFilter.IsExposedType(type))
                    {
                        if(type.NodeType == NodeType.Class)
                            this.PopulateDescendantIndex(type);

                        this.PopulateImplementorIndex(type);
                    }
                }
            }

            // Start the document
            writer.WriteStartDocument();
            writer.WriteStartElement("reflection");

            // Write assembly info
            writer.WriteStartElement("assemblies");

            foreach(AssemblyNode assembly in this.Assemblies)
                this.WriteAssembly(assembly);

            writer.WriteEndElement();

            // Start API info
            writer.WriteStartElement("apis");

            this.StartElementCallbacks("apis", spaces);

            // Write out information for each namespace.  The overall list of namespaces is part of the document
            // model XSL transformation and isn't generated here.
            base.VisitNamespaces(spaces);

            // Finish API info
            this.EndElementCallbacks("apis", spaces);

            writer.WriteEndElement();

            // Finish document
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        /// <inheritdoc />
        protected override void VisitNamespace(Namespace space)
        {
            namespaceCount++;

            this.WriteNamespace(space);
            base.VisitNamespace(space);
        }

        /// <inheritdoc />
        protected override void VisitType(TypeNode type)
        {
            if(type == null)
                throw new ArgumentNullException(nameof(type));

            SourceContext typeSourceContext = new(new Document(), -1, -1);
            bool typeSourceContextSet = false;

            typeCount++;

            // Load source context information for each member.  Since there's no way to get the source file
            // for a type from a PDB, we'll rely on one of the members to provide that information if possible.
            if(this.SourceCodeBasePath != null && this.ApiFilter.IsExposedType(type))
            {
                foreach(Member member in type.Members.Where(m => this.ApiFilter.IsExposedMember(m)))
                {
                    Method method = null;

                    switch(member.NodeType)
                    {
                        case NodeType.InstanceInitializer:
                        case NodeType.StaticInitializer:
                        case NodeType.Method:
                            method = (Method)member;
                            this.SetSourceContext(method);
                            break;

                        case NodeType.Property:
                            Property p = (Property)member;

                            if(p.Getter != null)
                            {
                                method = p.Getter;
                                this.SetSourceContext(method);
                            }
                            else
                                if(p.Setter != null)
                                {
                                    method = p.Setter;
                                    this.SetSourceContext(method);
                                }
                            break;

                        default:
                            break;
                    }

                    if(!typeSourceContextSet && method != null && method.FirstLineContext.Document != null &&
                      !String.IsNullOrWhiteSpace(method.FirstLineContext.Document.Name))
                    {
                        typeSourceContext.Document.Name = method.FirstLineContext.Document.Name;
                        typeSourceContextSet = true;
                    }
                }

                if(!typeSourceContextSet)
                {
                    typeSourceContext.Document.Name = this.FindSourceFileForType(type.FullName);

                    if(typeSourceContext.Document.Name == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Source code location not found for " + type.FullName);
                        this.MessageLogger(this.WarnOnMissingContext ? MessageLevel.Warn : MessageLevel.Info,
                            $"Source code location not found for {type.FullName}");
                    }
                }

                type.SourceContext = typeSourceContext;
            }

            this.WriteType(type);
            base.VisitType(type);
        }

        /// <summary>
        /// This loads the source code context for the given method
        /// </summary>
        /// <param name="method">The method for which to load source context info</param>
        private void SetSourceContext(Method method)
        {
            if(method.DeclaringType.DeclaringModule.reader.PdbExists && method.ProviderHandle is int providerHandle &&
              method.FirstLineContext.Document == null)
            {
                uint token = (uint)providerHandle | 0x06000000;
                method.DeclaringType.DeclaringModule.reader.GetMethodDebugSymbols(method, token);

                if(method.FirstLineContext.Document != null)
                {
                    string sourceFile = method.FirstLineContext.Document.Name;

                    if(sourceFile.StartsWith(this.SourceCodeBasePath, StringComparison.OrdinalIgnoreCase))
                        method.FirstLineContext.Document.Name = sourceFile.Substring(this.SourceCodeBasePath.Length);
                    else
                        method.FirstLineContext.Document.Name = String.Empty;
                }
            }
        }

        /// <summary>
        /// This is used to try and find the source code file for the given type name by searching the source
        /// path for a like-named file.
        /// </summary>
        /// <param name="fullTypeName">The full type name for which to try and locate a source code file</param>
        /// <returns>The filename if successful or null if not</returns>
        /// <remarks>This is typically needed for empty classes, interfaces, and enumerations which don't have
        /// sequence points in the PDB file.  This makes the assumption that the source code is structured in
        /// folders named after the namespaces with each file named after the class it represents.</remarks>
        private string FindSourceFileForType(string fullTypeName)
        {
            Stack<string> typeNameParts = new(fullTypeName.Split('.'));
            string sourceFile = null, searchPattern = typeNameParts.Pop();

            // For nested types, look for the parent type
            int pos = searchPattern.IndexOf('+');

            if(pos != -1)
                searchPattern = searchPattern.Substring(0, pos);

            try
            {
                var matches = Directory.EnumerateFiles(this.SourceCodeBasePath, searchPattern + "*",
                    SearchOption.AllDirectories).ToList();

                // If we didn't get a match and the name contains a template parameter count, remove it and
                // try again.
                if(matches.Count == 0)
                {
                    pos = searchPattern.IndexOf('`');

                    if(pos != -1)
                    {
                        searchPattern = searchPattern.Substring(0, pos);

                        matches = [.. Directory.EnumerateFiles(this.SourceCodeBasePath, searchPattern + "*",
                            SearchOption.AllDirectories)];
                    }
                }

                if(matches.Count > 1)
                {
                    // Look for an exact match by type name
                    matches = [.. matches.Where(m => Path.GetFileNameWithoutExtension(m).EndsWith(searchPattern,
                        StringComparison.OrdinalIgnoreCase))];

                    if(matches.Count != 1)
                    {
                        // If more than one match was found, add namespace parts to further qualify the name
                        // to see if we can find a single best match.
                        while(typeNameParts.Count != 0)
                        {
                            searchPattern = Path.Combine(typeNameParts.Pop(), searchPattern);

                            var namespaceMatches = matches.Where(m => Path.Combine(
                                Path.GetDirectoryName(m), Path.GetFileNameWithoutExtension(m)).EndsWith(
                                searchPattern, StringComparison.OrdinalIgnoreCase)).ToList();

                            if(namespaceMatches.Count == 1)
                            {
                                sourceFile = namespaceMatches[0];
                                break;
                            }

                            if(namespaceMatches.Count == 0)
                                break;
                        }
                    }
                    else
                        sourceFile = matches[0];
                }
                else
                {
                    if(matches.Count == 1 && Path.GetFileNameWithoutExtension(matches[0]).EndsWith(searchPattern,
                      StringComparison.OrdinalIgnoreCase))
                    {
                        sourceFile = matches[0];
                    }
                }

                if(sourceFile != null && sourceFile.StartsWith(this.SourceCodeBasePath, StringComparison.OrdinalIgnoreCase))
                    sourceFile = sourceFile.Substring(this.SourceCodeBasePath.Length);
            }
            catch(Exception ex)
            {
                // Ignore any exceptions
                System.Diagnostics.Debug.WriteLine(ex);
            }

            return sourceFile;
        }

        /// <inheritdoc />
        protected override void VisitMember(Member member)
        {
            memberCount++;

            writer.WriteStartElement("api");

            // !EFW - Change from ComponentOne
            writer.WriteAttributeString("id", namer.GetMemberName(member).TranslateToValidXmlValue());

            this.StartElementCallbacks("api", member);

            this.WriteMember(member, true);

            this.EndElementCallbacks("api", member);

            writer.WriteEndElement();
        }
        #endregion

        #region Tag callback methods
        //=====================================================================

        /// <summary>
        /// Register a start tag callback
        /// </summary>
        /// <param name="name">The name of the element to which the callback relates</param>
        /// <param name="callback">The callback to invoke</param>
        public void RegisterStartTagCallback(string name, MRefBuilderCallback callback)
        {
            if(!startTagCallbacks.TryGetValue(name, out List<MRefBuilderCallback> current))
            {
                current = [];
                startTagCallbacks.Add(name, current);
            }

            current.Add(callback);
        }

        /// <summary>
        /// Register an end tag callback
        /// </summary>
        /// <param name="name">The name of the element to which the callback relates</param>
        /// <param name="callback">The callback to invoke</param>
        public void RegisterEndTagCallback(string name, MRefBuilderCallback callback)
        {
            if(!endTagCallbacks.TryGetValue(name, out List<MRefBuilderCallback> current))
            {
                current = [];
                endTagCallbacks.Add(name, current);
            }

            current.Add(callback);
        }

        /// <summary>
        /// Invoke the callbacks for the given start tag
        /// </summary>
        /// <param name="name">The start tag for which to invoke callbacks</param>
        /// <param name="info">The information to pass to the callbacks</param>
        private void StartElementCallbacks(string name, object info)
        {
            if(startTagCallbacks.TryGetValue(name, out List<MRefBuilderCallback> callbacks))
            {
                foreach(MRefBuilderCallback callback in callbacks)
                    callback(writer, info);
            }
        }

        /// <summary>
        /// Invoke the callbacks for the given end tag
        /// </summary>
        /// <param name="name">The end tag for which to invoke callbacks</param>
        /// <param name="info">The information to pass to the callbacks</param>
        private void EndElementCallbacks(string name, object info)
        {
            if(endTagCallbacks.TryGetValue(name, out List<MRefBuilderCallback> callbacks))
            {
                foreach(MRefBuilderCallback callback in callbacks)
                    callback(writer, info);
            }
        }
        #endregion

        #region General helper methods
        //=====================================================================

        /// <summary>
        /// This is used to get an enumerable list of exposed interfaces
        /// </summary>
        /// <param name="contracts">The list of interfaces</param>
        /// <returns>An enumerable list of exposed interfaces</returns>
        private IEnumerable<Interface> GetExposedInterfaces(InterfaceList contracts)
        {
            foreach(var contract in contracts)
                if(this.ApiFilter.IsDocumentedInterface(contract))
                    yield return contract;
        }

        /// <summary>
        /// This is used to get a list of exposed implemented members
        /// </summary>
        /// <param name="members">An enumerable list of members to check</param>
        /// <returns>An enumerable list containing just the exposed, implemented members</returns>
        private IEnumerable<Member> GetExposedImplementedMembers(IEnumerable<Member> members)
        {
            foreach(Member member in members)
            {
                if(this.ApiFilter.IsExposedMember(member))
                    yield return member;
            }
        }

        /// <summary>
        /// This is used to get an enumerable list of the exposed attributes on a member
        /// </summary>
        /// <param name="attributes">The general attributes</param>
        /// <returns>An enumerable list of the exposed attributes on the member</returns>
        private IEnumerable<AttributeNode> GetExposedAttributes(AttributeList attributes)
        {
            if(attributes != null)
            {
                foreach(var attribute in attributes)
                {
                    if(attribute == null)
                        throw new InvalidOperationException("Null attribute found");

                    if(this.ApiFilter.IsExposedAttribute(attribute))
                        yield return attribute;
                }
            }
        }

        /// <summary>
        /// Get an enumerable list of applied fields from an enumeration type
        /// </summary>
        /// <param name="enumeration">The enumeration from which to get the fields</param>
        /// <param name="value">The value to use in determining the applied fields</param>
        /// <returns>An enumerable list of fields from the enumeration that appear in the value</returns>
        private static FieldList GetAppliedFields(EnumNode enumeration, long value)
        {
            FieldList list = [];
            MemberList members = enumeration.Members;

            foreach(var member in members)
            {
                if(member.NodeType != NodeType.Field)
                    continue;

                Field field = (Field)member;

                if(field.DefaultValue != null)
                {
                   long fieldValue;

                    if(field.DefaultValue.Value is ulong defValue)
                        fieldValue = unchecked((long)defValue);
                    else
                        fieldValue = Convert.ToInt64(field.DefaultValue.Value, CultureInfo.InvariantCulture);

                    // If a single field matches, return it.  Otherwise return all fields that are in the value
                    // except zero.
                    if(fieldValue == value)
                        return [field];

                    if(fieldValue != 0 && (fieldValue & value) == fieldValue)
                        list.Add(field);
                }
            }

            // Remove duplicates that are in combo values. For example, in the set A, B, AplusB, remove A and B
            // because they are present in the combined value AplusB).
            for(int i = 0; i < list.Count; i++)
            {
                long fieldValue;

                if(list[i].DefaultValue.Value is ulong defValue)
                    fieldValue = unchecked((long)defValue);
                else
                    fieldValue = Convert.ToInt64(list[i].DefaultValue.Value, CultureInfo.InvariantCulture);

                if(list.Skip(i + 1).Any(f =>
                {
                    long compare;

                    if (f.DefaultValue.Value is ulong fieldDefValue)
                        compare = unchecked((long)fieldDefValue);
                    else
                        compare = Convert.ToInt64(f.DefaultValue.Value, CultureInfo.InvariantCulture);

                    return ((fieldValue & compare) == fieldValue);
                }))
                {
                    list.RemoveAt(i);
                    i--;
                }
            }

            return list;
        }

        /// <summary>
        /// This is used to create an index of parent types and their descendants
        /// </summary>
        /// <param name="type">The descendant type to add to the index</param>
        private void PopulateDescendantIndex(TypeNode child)
        {
            TypeNode parent = child.BaseType;

            if(parent != null)
            {
                // Unspecialize the parent so we see specialized types as children
                parent = parent.GetTemplateType();

                // Get the list of children for that parent (i.e. the sibling list)
                if(!descendantIndex.TryGetValue(parent, out List<TypeNode> siblings))
                {
                    siblings = [];
                    descendantIndex[parent] = siblings;
                }

                // Add the type in question to the sibling list
                siblings.Add(child);
            }
        }

        /// <summary>
        /// This is used to create an index of interfaces and the types that implement them
        /// </summary>
        /// <param name="type">The type to add to the index</param>
        private void PopulateImplementorIndex(TypeNode type)
        {
            foreach(var i in this.GetExposedInterfaces(type.Interfaces))
            {
                var contract = i;

                // Get the unspecialized form of the interface
                if(contract.IsGeneric)
                    contract = (Interface)contract.GetTemplateType();

                // Get the list of implementors
                if(!implementorIndex.TryGetValue(contract, out List<TypeNode> implementors))
                {
                    implementors = [];
                    implementorIndex[contract] = implementors;
                }

                // Add the type to it
                implementors.Add(type);
            }
        }
        #endregion

        #region Writer methods for assemblies
        //=====================================================================

        /// <summary>
        /// Write out information for an assembly
        /// </summary>
        /// <param name="assembly">The assembly to write</param>
        private void WriteAssembly(AssemblyNode assembly)
        {
            writer.WriteStartElement("assembly");

            this.WriteStringAttribute("name", assembly.Name);

            // Basic assembly data
            writer.WriteStartElement("assemblydata");
            this.WriteStringAttribute("version", assembly.Version.ToString());
            this.WriteStringAttribute("culture", assembly.Culture.ToString());

            byte[] key = assembly.PublicKeyOrToken;

            writer.WriteStartAttribute("key");
            writer.WriteBinHex(key, 0, key.Length);
            writer.WriteEndAttribute();

            this.WriteStringAttribute("hash", assembly.HashAlgorithm.ToString());
            writer.WriteEndElement();

            // Assembly attribute data
            this.WriteAttributes(assembly.Attributes);

            writer.WriteEndElement();
        }
        #endregion

        #region Writer methods for namespaces
        //=====================================================================

        /// <summary>
        /// Write out information for a namespace
        /// </summary>
        /// <param name="space">The namespace to write</param>
        private void WriteNamespace(Namespace space)
        {
            writer.WriteStartElement("api");

            // !EFW - Change from ComponentOne
            writer.WriteAttributeString("id", namer.GetNamespaceName(space).TranslateToValidXmlValue());

            this.StartElementCallbacks("api", space);

            this.WriteApiData(space);
            this.WriteNamespaceElements(space);

            this.EndElementCallbacks("api", space);

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out a list of namespace elements
        /// </summary>
        /// <param name="space">The namespace for which to write out elements</param>
        private void WriteNamespaceElements(Namespace space)
        {
            TypeNodeList types = space.Types;

            if(types.Count != 0)
            {
                writer.WriteStartElement("elements");

                // Skip hidden types but if a type is not exposed and has exposed members we must add it.  Also,
                // group by type name and only add the first entry.  If multiple assemblies are documented and
                // they contain duplicate types, we get duplicate type entries.  The duplicate types and members
                // will be merged after the file has been created.  It's easier to do it that way than when the
                // types are visited during the file's creation.
                foreach(var type in types.Where(t => this.ApiFilter.IsExposedType(t) ||
                  this.ApiFilter.HasExposedMembers(t)).GroupBy(t => t.FullName).Select(g => g.First()))
                {
                    writer.WriteStartElement("element");

                    // !EFW - Change from ComponentOne
                    writer.WriteAttributeString("api", namer.GetTypeName(type).TranslateToValidXmlValue());

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out a namespace reference
        /// </summary>
        /// <param name="space">The namespace to reference</param>
        private void WriteNamespaceReference(Namespace space)
        {
            writer.WriteStartElement("namespace");

            // !EFW - Change from ComponentOne
            writer.WriteAttributeString("api", namer.GetNamespaceName(space).TranslateToValidXmlValue());

            writer.WriteEndElement();
        }
        #endregion

        #region Writer methods for types
        //=====================================================================

        /// <summary>
        /// Write out information for a type
        /// </summary>
        /// <param name="type">The type to write</param>
        private void WriteType(TypeNode type)
        {
            writer.WriteStartElement("api");

            // !EFW - Change from ComponentOne
            writer.WriteAttributeString("id", namer.GetTypeName(type).TranslateToValidXmlValue());
#if DEBUG_SHOW_NULLABLE_STATE
            writer.WriteAttributeString("nullableContext", type.NullableContext.ToString());
#endif            
            this.StartElementCallbacks("api", type);

            this.WriteApiData(type);
            this.WriteSourceContext(type.SourceContext, type.SourceContext);
            this.WriteTypeData(type);

            switch(type.NodeType)
            {
                case NodeType.Class:
                case NodeType.Struct:
                    this.WriteGenericParameters(type.TemplateParameters);
                    this.WriteInterfaces(type.Interfaces);
                    this.WriteTypeElements(type);
                    break;

                case NodeType.Interface:
                    this.WriteGenericParameters(type.TemplateParameters);
                    this.WriteInterfaces(type.Interfaces);
                    this.WriteImplementors((Interface)type);
                    this.WriteTypeElements(type);
                    break;

                case NodeType.DelegateNode:
                    DelegateNode handler = (DelegateNode)type;
                    AttributeList retValAttributes = null;

                    if(handler.Members.FirstOrDefault(m => m.Name.Name.Equals("EndInvoke", StringComparison.Ordinal)) is Method endInvoke)
                        retValAttributes = endInvoke.ReturnAttributes;

                    this.WriteGenericParameters(handler.TemplateParameters);
                    this.WriteParameters(handler.Parameters);

                    this.WriteReturnValue(handler.ReturnType, retValAttributes,
                        DetermineNullableState(handler, retValAttributes).GetEnumerator());
                    break;

                case NodeType.EnumNode:
                    this.WriteEnumerationData((EnumNode)type);
                    this.WriteTypeElements(type);
                    break;
            }

            this.WriteTypeContainers(type);
            this.WriteAttributes(type.Attributes);

            this.EndElementCallbacks("api", type);
            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out type data for a type
        /// </summary>
        /// <param name="type">The type for which to write type data</param>
        private void WriteTypeData(TypeNode type)
        {
            writer.WriteStartElement("typedata");

            // Data for all types
            this.WriteStringAttribute("visibility", this.ApiFilter.GetVisibility(type));
            this.WriteBooleanAttribute("abstract", type.IsAbstract, false);
            this.WriteBooleanAttribute("sealed", type.IsSealed, false);
            this.WriteBooleanAttribute("serializable", (type.Flags & TypeFlags.Serializable) != 0);

            // Interop attribute data.  In code, these are attributes.  However, the compiler converts the
            // attribute data to type metadata so we don't see them in the regular attribute list.  The metadata
            // for the attributes is too complex to reconstruct so writing the info out as type data is much
            // simpler and can be handled by the syntax components as needed.
            if(this.ApiFilter.IncludeAttributes)
            {
                // ComImportAttribute
                if((type.Flags & TypeFlags.Import) != 0)
                    this.WriteBooleanAttribute("comimport", true);

                // StructLayoutAttribute.  If layout kind, class size, or packing size are set, assume it is
                // used.  We ignore character set as it may vary by compiler (default is Auto but C# sets it to
                // ANSI).  Structures are marked with a sequential layout and those with no members get a size of
                // one so we'll ignore the attribute in those cases too since the user didn't add them.
                if(((type.Flags & TypeFlags.LayoutMask) != 0 || type.ClassSize != 0 || type.PackingSize != 0) &&
                  (type is not Struct || type.ClassSize > 1))
                {
                    switch(type.Flags & TypeFlags.LayoutMask)
                    {
                        case TypeFlags.AutoLayout:
                            this.WriteStringAttribute("layout", "auto");
                            break;

                        case TypeFlags.SequentialLayout:
                            this.WriteStringAttribute("layout", "sequential");
                            break;

                        case TypeFlags.ExplicitLayout:
                            this.WriteStringAttribute("layout", "explicit");
                            break;
                    }

                    if(type.ClassSize != 0)
                        this.WriteStringAttribute("size", type.ClassSize.ToString(CultureInfo.InvariantCulture));

                    if(type.PackingSize != 0)
                        this.WriteStringAttribute("pack", type.PackingSize.ToString(CultureInfo.InvariantCulture));

                    // Equivalent to CharSet but it's been "format" forever so we won't change it now
                    switch(type.Flags & TypeFlags.StringFormatMask)
                    {
                        case TypeFlags.AnsiClass:
                            this.WriteStringAttribute("format", "ansi");
                            break;

                        case TypeFlags.UnicodeClass:
                            this.WriteStringAttribute("format", "unicode");
                            break;

                        case TypeFlags.AutoClass:
                            this.WriteStringAttribute("format", "auto");
                            break;
                    }
                }
            }

            // XAML syntax properties
            bool hasContentProperty = false;

            if(type.NodeType == NodeType.Class || type.NodeType == NodeType.Struct)
            {
                var defaultConstructor = type.GetConstructors().Cast<Method>().FirstOrDefault(m => m.IsPublic &&
                    m.Parameters.Count == 0);

                if(defaultConstructor != null && this.ApiFilter.IsExposedMember(defaultConstructor))
                    this.WriteStringAttribute("defaultConstructor", namer.GetMemberName(defaultConstructor).TranslateToValidXmlValue());

                var contentProperty = type.Attributes.FirstOrDefault(a => a.Type.Name.Name == "ContentPropertyAttribute" &&
                    a.Expressions.Count == 1);

                if(contentProperty != null && contentProperty.Expressions[0] is Literal l)
                {
                    this.WriteStringAttribute("contentProperty", ("P:" + type.FullName + "." + l.Value.ToString()).TranslateToValidXmlValue());
                    hasContentProperty = true;
                }

                if(type.NodeType == NodeType.Struct && !type.Members.Where(m => m.NodeType == NodeType.Property).Cast<Property>().Any(
                  p => p.IsPublic && p.Setter != null && p.Setter.IsPublic))
                {
                    this.WriteStringAttribute("noSettableProperties", "true");
                }
            }

            this.StartElementCallbacks("typedata", type);
            this.EndElementCallbacks("typedata", type);

            writer.WriteEndElement();

            // For classes and structures, record base types and descendants
            if(type is Class || type is Struct)
                this.WriteHierarchy(type, hasContentProperty);
        }

        /// <summary>
        /// Write out the elements for a type
        /// </summary>
        /// <param name="type">The type for which to write out elements</param>
        private void WriteTypeElements(TypeNode type)
        {
            MemberDictionary members = new(type, this.ApiFilter);

            if(members.Count != 0)
            {
                writer.WriteStartElement("elements");
                this.StartElementCallbacks("elements", members);

                foreach(Member member in members)
                {
                    writer.WriteStartElement("element");

                    Member template = member.GetTemplateMember();

                    // !EFW - Change from ComponentOne
                    writer.WriteAttributeString("api", namer.GetMemberName(template).TranslateToValidXmlValue());

                    bool write = false;

                    // Inherited, specialized generics get a displayed target different from the target.  We also
                    // write out their info since it can't be looked up anywhere.
                    if(!member.DeclaringType.IsStructurallyEquivalentTo(template.DeclaringType))
                    {
                        // !EFW - Change from ComponentOne
                        writer.WriteAttributeString("display-api", namer.GetMemberName(member).TranslateToValidXmlValue());
                        write = true;
                    }

                    // If a member is from a type in a dependency assembly, write out its info, since it can't be
                    // looked up in this file.
                    if(!assemblyNames.Contains(member.DeclaringType.DeclaringModule.ContainingAssembly.StrongName))
                        write = true;

                    if(write)
                        this.WriteMember(member, false);

                    writer.WriteEndElement();
                }

                this.EndElementCallbacks("elements", members);
                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out the hierarchy for a type
        /// </summary>
        /// <param name="type">The type for which to write the hierarchy</param>
        /// <param name="hasContentProperty">True if the type has a XAML content property, false if not</param>
        private void WriteHierarchy(TypeNode type, bool hasContentProperty)
        {
            writer.WriteStartElement("family");

            // Write ancestors
            writer.WriteStartElement("ancestors");

            for(TypeNode ancestor = type.BaseType; ancestor != null; ancestor = ancestor.BaseType)
                this.WriteTypeReference(ancestor, null, null, null, NullableState.NotSpecified, !hasContentProperty);

            writer.WriteEndElement();

            // Write descendants
            if(descendantIndex.TryGetValue(type, out List<TypeNode> descendants))
            {
                // Yes, it's misspelled but it's been that way for years and changing it would break all the
                // presentation styles so we'll leave it alone.
                writer.WriteStartElement("descendents");

                foreach(TypeNode descendant in descendants)
                    this.WriteTypeReference(descendant);

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out implementors of an interface
        /// </summary>
        /// <param name="contract"></param>
        private void WriteImplementors(Interface contract)
        {
            if(!implementorIndex.TryGetValue(contract, out List<TypeNode> implementors))
                return;

            if(implementors != null && implementors.Count != 0)
            {
                writer.WriteStartElement("implementors");

                this.StartElementCallbacks("implementors", implementors);

                foreach(TypeNode implementor in implementors)
                    this.WriteTypeReference(implementor);

                writer.WriteEndElement();

                this.EndElementCallbacks("implementors", implementors);
            }
        }

        /// <summary>
        /// Write out enumeration data
        /// </summary>
        /// <param name="enumeration">The enumeration for which to write out data</param>
        private void WriteEnumerationData(EnumNode enumeration)
        {
            TypeNode underlying = enumeration.UnderlyingType;

            if(underlying.FullName != "System.Int32")
            {
                writer.WriteStartElement("enumerationbase");
                this.WriteTypeReference(underlying);
                writer.WriteEndElement();
            }
        }
        #endregion

        #region Writer methods for members
        //=====================================================================

        /// <summary>
        /// Write out information for a member
        /// </summary>
        /// <param name="member">The member to write out.</param>
        /// <param name="includeSourceContext">True to the include source code context when possible, false to
        /// omit it.</param>
        public void WriteMember(Member member, bool includeSourceContext)
        {
            if(member == null)
                throw new ArgumentNullException(nameof(member));

            this.WriteMember(member, member.DeclaringType, includeSourceContext);
        }

        /// <summary>
        /// Write out information for a member
        /// </summary>
        /// <param name="member">The member to write out.</param>
        /// <param name="type">The declaring type of the member.</param>
        /// <param name="includeSourceContext">True to the include source code context when possible, false to
        /// omit it.</param>
        private void WriteMember(Member member, TypeNode type, bool includeSourceCodeContext)
        {
            this.WriteApiData(member);
            this.WriteMemberData(member);

            switch(member.NodeType)
            {
                case NodeType.Field:
                    Field field = (Field)member;

                    // Fields never have sequence points in the PDB so use the type's source context
                    if(includeSourceCodeContext)
                        this.WriteSourceContext(member.DeclaringType.SourceContext, member.DeclaringType.SourceContext);

                    this.WriteFieldData(field);
                    this.WriteReturnValue(field.Type, field.Attributes, field.NullableStates.GetEnumerator());

                    // Write enumeration and literal (constant) field values
                    if(field.DeclaringType.NodeType == NodeType.EnumNode)
                    {
                        this.WriteLiteral(new Literal(field.DefaultValue.Value,
                            ((EnumNode)field.DeclaringType).UnderlyingType), false);
                    }
                    else
                    {
                        if(field.IsLiteral)
                            this.WriteLiteral(new Literal(field.DefaultValue.Value, field.Type), false);
                        else
                        {
                            // Constant decimal values are stored in an attribute
                            if(field.Type.FullName == "System.Decimal")
                            {
                                var decAttr = field.Attributes.FirstOrDefault(
                                    a => a.Type.FullName == "System.Runtime.CompilerServices.DecimalConstantAttribute");
                                decimal d;

                                try
                                {
                                    if((decAttr?.Expressions?.Count ?? 0) == 5)
                                    {
                                        // Parameter order: scale, sign, hi, mid, low
                                        var literals = decAttr.Expressions.Select(e => ((Literal)e).Value).ToArray();

                                        // Call the constructor based on the hi value type which may be int or uint
                                        if(literals[2].GetType() == typeof(int))
                                        {
                                            d = new decimal((int)literals[4], (int)literals[3], (int)literals[2],
                                                (byte)literals[1] != 0, (byte)literals[0]);
                                        }
                                        else
                                        {
                                            d = new decimal((int)(uint)literals[4], (int)(uint)literals[3],
                                                (int)(uint)literals[2], (byte)literals[1] != 0, (byte)literals[0]);
                                        }

                                        this.WriteLiteral(new Literal(d, field.Type), false);
                                    }
                                }
                                catch(Exception ex)
                                {
                                    // Ignore exceptions trying to parse decimal constants
                                    Debug.WriteLine(ex);
                                }
                            }
                        }
                    }
                    break;

                case NodeType.Method:
                    Method method = (Method)member;

                    if(includeSourceCodeContext)
                        this.WriteSourceContext(method.FirstLineContext, member.DeclaringType.SourceContext);

                    this.WriteProcedureData(method, method.OverriddenMember);

                    // Write the templates node with either the generic template parameters or the specialized
                    // template arguments.
                    if(method.TemplateArguments != null)
                        this.WriteSpecializedTemplateArguments(method.TemplateArguments);
                    else
                        this.WriteGenericParameters(method.TemplateParameters);

                    this.WriteParameters(method.Parameters);
                    this.WriteReturnValue(method.ReturnType, method.ReturnAttributes,
                        DetermineNullableState(method, method.ReturnAttributes).GetEnumerator());
                    this.WriteImplementedMembers(method.GetImplementedMethods());
                    break;

                case NodeType.Property:
                    Property property = (Property)member;

                    if(includeSourceCodeContext)
                        if(property.Getter != null)
                            this.WriteSourceContext(property.Getter.FirstLineContext, member.DeclaringType.SourceContext);
                        else
                            if(property.Setter != null)
                                this.WriteSourceContext(property.Setter.FirstLineContext, member.DeclaringType.SourceContext);

                    this.WritePropertyData(property);
                    this.WriteParameters(property.Parameters);
                    this.WriteReturnValue(property.Type, property.Attributes,
                        DetermineNullableState(property.DeclaringType, property.Attributes).GetEnumerator());
                    this.WriteImplementedMembers(property.GetImplementedProperties());
                    break;

                case NodeType.Event:
                    Event trigger = (Event)member;

                    // Events never have sequence points in the PDB so use the type's source context
                    if(includeSourceCodeContext)
                        this.WriteSourceContext(member.DeclaringType.SourceContext, member.DeclaringType.SourceContext);

                    this.WriteEventData(trigger);
                    this.WriteImplementedMembers(trigger.GetImplementedEvents());
                    break;

                case NodeType.InstanceInitializer:
                case NodeType.StaticInitializer:
                    Method constructor = (Method)member;

                    if(includeSourceCodeContext)
                        this.WriteSourceContext(constructor.FirstLineContext, member.DeclaringType.SourceContext);

                    this.WriteParameters(constructor.Parameters);
                    break;
            }

            this.WriteMemberContainers(type);
            this.WriteAttributes(member.Attributes);
        }

        /// <summary>
        /// Write out the source code context (filename and starting line number)
        /// </summary>
        /// <param name="sourceContext">The source context information</param>
        /// <param name="parentContext">The parent type's source context information.  This will be used if the
        /// member doesn't have a source document.  This happens for interface members.</param>
        private void WriteSourceContext(SourceContext sourceContext, SourceContext parentContext)
        {
            if(sourceContext.Document == null && parentContext.Document != null)
                sourceContext = parentContext;

            if(sourceContext.Document != null)
            {
                string sourceFile = sourceContext.Document.Name;

                if(!String.IsNullOrWhiteSpace(sourceFile))
                {
                    sourceFile = WebUtility.UrlEncode(sourceFile).Replace("%5C", "/");

                    writer.WriteStartElement("sourceContext");
                    this.WriteStringAttribute("file", sourceFile);

                    if(sourceContext.StartPos != -1)
                        writer.WriteAttributeString("startLine", sourceContext.StartLine.ToString(CultureInfo.InvariantCulture));

                    writer.WriteEndElement();
                }
            }
        }

        /// <summary>
        /// Write member data such as visibility, etc.
        /// </summary>
        /// <param name="member">The member for which to write data</param>
        private void WriteMemberData(Member member)
        {
            writer.WriteStartElement("memberdata");

            this.WriteStringAttribute("visibility", this.ApiFilter.GetVisibility(member));
            this.WriteBooleanAttribute("static", member.IsStatic, false);
            this.WriteBooleanAttribute("special", member.IsSpecialName, false);

            // Nothing is done regarding overloads.  That is a document model concept and may need to be
            // tweaked after versioning.  Overloads are handled by the document model XSL transformations.

            this.WriteBooleanAttribute("default", member.IsDefaultMember(), false);

            this.StartElementCallbacks("memberdata", member);
            this.EndElementCallbacks("memberdata", member);

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out field data
        /// </summary>
        /// <param name="field">The field for which to write data</param>
        private void WriteFieldData(Field field)
        {
            writer.WriteStartElement("fielddata");

            // Init only decimals with a DecimalConstantValue attribute are constants
            if(field.IsLiteral || !field.IsInitOnly || field.Type.FullName != "System.Decimal" ||
              !field.Attributes.Any(a => a.Type.FullName == "System.Runtime.CompilerServices.DecimalConstantAttribute"))
            {
                this.WriteBooleanAttribute("literal", field.IsLiteral);
                this.WriteBooleanAttribute("initonly", field.IsInitOnly);
            }
            else
            {
                this.WriteBooleanAttribute("literal", true);
                this.WriteBooleanAttribute("initonly", false);
            }

            this.WriteBooleanAttribute("volatile", field.IsVolatile, false);
            this.WriteBooleanAttribute("serialized", (field.Flags & FieldFlags.NotSerialized) == 0);

            // FieldOffsetAttribute.  In code, this is an attribute.  However, the compiler converts the
            // attribute to metadata so we don't see it in the regular attribute list.  The metadata for the
            // attribute is too complex to reconstruct so writing the info out as field data is much simpler
            // and can be handled by the syntax components as needed.
            if(this.ApiFilter.IncludeAttributes && (field.Offset != 0 ||
              (field.DeclaringType.Flags & TypeFlags.ExplicitLayout) != 0))
            {
                this.WriteStringAttribute("offset", field.Offset.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out specialized template arguments
        /// </summary>
        /// <param name="templateArguments">The template arguments to write</param>
        private void WriteSpecializedTemplateArguments(TypeNodeList templateArguments)
        {
            if(templateArguments != null && templateArguments.Count != 0)
            {
                writer.WriteStartElement("templates");

                foreach(var ta in templateArguments)
                    this.WriteTypeReference(ta);

                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out implemented members
        /// </summary>
        /// <param name="members">An enumerable list of implemented members</param>
        private void WriteImplementedMembers(IEnumerable<Member> members)
        {
            if(members.Any())
            {
                var exposedMembers = this.GetExposedImplementedMembers(members);

                if(exposedMembers.Any())
                {
                    writer.WriteStartElement("implements");

                    foreach(var member in exposedMembers)
                        this.WriteMemberReference(member);

                    writer.WriteEndElement();
                }
            }
        }

        /// <summary>
        /// Write out property data
        /// </summary>
        /// <param name="property">The property for which to write out data</param>
        private void WritePropertyData(Property property)
        {
            string propertyVisibility = this.ApiFilter.GetVisibility(property);

            Method getter = property.Getter, setter = property.Setter;

            // Procedure data
            this.WriteProcedureData(getter ?? setter, property.OverriddenMember);

            // Property data
            writer.WriteStartElement("propertydata");

            if(getter != null)
            {
                if(this.ApiFilter.IsVisible(getter))
                {
                    this.WriteBooleanAttribute("get", true);

                    string getterVisibility = this.ApiFilter.GetVisibility(getter);

                    if(getterVisibility != propertyVisibility)
                        this.WriteStringAttribute("get-visibility", getterVisibility);
                }
                else
                    getter = null;
            }

            if(setter != null)
            {
                if(this.ApiFilter.IsVisible(setter))
                {
                    this.WriteBooleanAttribute("set", true);

                    if(setter.ReturnType != null && setter.ReturnType.StructuralElementTypes != null &&
                      setter.ReturnType.StructuralElementTypes.Any(s => s.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
                    {
                        this.WriteBooleanAttribute("initOnly", true);
                    }

                    string setterVisibility = this.ApiFilter.GetVisibility(setter);

                    if(setterVisibility != propertyVisibility)
                        this.WriteStringAttribute("set-visibility", setterVisibility);
                }
                else
                    setter = null;
            }

            writer.WriteEndElement();

            if(getter != null)
            {
                writer.WriteStartElement("getter");

                this.WriteStringAttribute("name", "get_" + property.Name.Name);
                this.WriteAttributes(getter.Attributes);

                writer.WriteEndElement();
            }

            if(setter != null)
            {
                writer.WriteStartElement("setter");

                this.WriteStringAttribute("name", "set_" + property.Name.Name.ToString());
                this.WriteAttributes(setter.Attributes);

                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out event data
        /// </summary>
        /// <param name="trigger">The event for which to write data</param>
        private void WriteEventData(Event trigger)
        {
            Method adder = trigger.HandlerAdder, remover = trigger.HandlerRemover, caller = trigger.HandlerCaller;

            this.WriteProcedureData(adder, trigger.OverriddenMember);

            writer.WriteStartElement("eventdata");

            if(adder != null)
                this.WriteBooleanAttribute("add", true);

            if(remover != null)
                this.WriteBooleanAttribute("remove", true);

            if(caller != null)
                this.WriteBooleanAttribute("call", true);

            writer.WriteEndElement();

            if(adder != null)
            {
                writer.WriteStartElement("adder");

                this.WriteStringAttribute("name", "add_" + trigger.Name.Name);
                this.WriteAttributes(adder.Attributes);

                writer.WriteEndElement();
            }

            if(remover != null)
            {
                writer.WriteStartElement("remover");

                this.WriteStringAttribute("name", "remove_" + trigger.Name.Name);
                this.WriteAttributes(remover.Attributes);

                writer.WriteEndElement();
            }

            writer.WriteStartElement("eventhandler");
            this.WriteTypeReference(trigger.HandlerType);
            writer.WriteEndElement();

            // Handlers should always be delegates but I have seen a case where one is not, so check for this
            DelegateNode handler = trigger.HandlerType as DelegateNode;

            if(handler != null)
            {
                ParameterList parameters = handler.Parameters;

                if(parameters != null && parameters.Count == 2 && parameters[0].Type.FullName == "System.Object")
                {
                    writer.WriteStartElement("eventargs");
                    this.WriteTypeReference(parameters[1].Type);
                    writer.WriteEndElement();
                }
            }
        }

        /// <summary>
        /// Write out member containers
        /// </summary>
        /// <param name="type">The declaring type of the member</param>
        private void WriteMemberContainers(TypeNode type)
        {
            writer.WriteStartElement("containers");

            this.WriteLibraryReference(type.DeclaringModule);
            this.WriteNamespaceReference(type.GetNamespace());
            this.WriteTypeReference(type);

            writer.WriteEndElement();
        }
        #endregion

        #region Writer methods for various common elements
        //=====================================================================

        /// <summary>
        /// Write out API data such as the group, subgroup, etc.
        /// </summary>
        /// <param name="api"></param>
        private void WriteApiData(Member api)
        {
            writer.WriteStartElement("apidata");

            string name = api.Name.Name, group, subgroup = null, subsubgroup = null;

            if(api.NodeType == NodeType.Namespace)
                group = "namespace";
            else
            {
                TypeNode type = api as TypeNode;

                if(type != null)
                {
                    group = "type";

                    name = type.GetUnmangledNameWithoutTypeParameters();

                    switch(api.NodeType)
                    {
                        case NodeType.Class:
                            subgroup = "class";
                            break;

                        case NodeType.Struct:
                            subgroup = "structure";
                            break;

                        case NodeType.Interface:
                            subgroup = "interface";
                            break;

                        case NodeType.EnumNode:
                            subgroup = "enumeration";
                            break;

                        case NodeType.DelegateNode:
                            subgroup = "delegate";
                            break;
                    }
                }
                else
                {
                    group = "member";

                    switch(api.NodeType)
                    {
                        case NodeType.Field:
                            subgroup = "field";
                            break;

                        case NodeType.Property:
                            subgroup = "property";
                            break;

                        case NodeType.InstanceInitializer:
                        case NodeType.StaticInitializer:
                            subgroup = "constructor";
                            break;

                        case NodeType.Method:
                            subgroup = "method";

                            if(api.IsSpecialName && name.StartsWith("op_", StringComparison.Ordinal))
                            {
                                subsubgroup = "operator";
                                name = name.Substring(3);
                            }
                            break;

                        case NodeType.Event:
                            subgroup = "event";
                            break;
                    }

                    // Name of EIIs is just interface member name
                    int dotIndex = name.LastIndexOf('.');

                    if(dotIndex > 0)
                        name = name.Substring(dotIndex + 1);
                }
            }

            this.WriteStringAttribute("name", name);
            this.WriteStringAttribute("group", group);

            if(subgroup != null)
                this.WriteStringAttribute("subgroup", subgroup);

            if(subsubgroup != null)
                this.WriteStringAttribute("subsubgroup", subsubgroup);

            this.StartElementCallbacks("apidata", api);
            this.EndElementCallbacks("apidata", api);

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out a type reference
        /// </summary>
        /// <param name="type">The type to reference</param>
        /// <param name="elementName">An element name if the type reference is part of a type such as
        /// <c>ValueTuple</c> or null if not.</param>
        /// <param name="attributes">An optional list of attributes to check for tuple element names</param>
        /// <param name="nullableStates">An enumerable list of the nullable states for the type's parts</param>
        /// <param name="lastState">The last used nullable state.  If it's common to all parts, there will only
        /// be one entry that applies to all parts.</param>
        /// <param name="addContentProperty">True to add the content property attribute for XAML syntax or
        /// false to omit it.</param>
        public void WriteTypeReference(TypeNode type, string elementName = null, AttributeList attributes = null,
          IEnumerator<NullableState> nullableStates = null, NullableState lastState = NullableState.NotSpecified,
          bool addContentProperty = false)
        {
            if(type == null)
                throw new ArgumentNullException(nameof(type));

            string[] names = null;

            if(attributes != null)
            {
                // If the return value is a tuple, see if we can output the element names so that the
                // presentation styles can output them in the expected format.
                var tupleElementNames = attributes.FirstOrDefault(a => a.Type.Name.Name == "TupleElementNamesAttribute");

                if(tupleElementNames != null && tupleElementNames.Expressions.Count != 0)
                {
                    if(tupleElementNames.Expressions[0] is Literal exp)
                    {
                        names = exp.Value as string[];

                        if(names != null)
                            SetElementNames(type, names);
                    }
                }
            }

            this.WriteStartTypeReference(type, elementName, nullableStates, lastState, addContentProperty);
            writer.WriteEndElement();
        }

        /// <summary>
        /// This is used to set the element names on a <c>ValueTuple</c>
        /// </summary>
        /// <param name="type">The type in which to find the <c>ValueTuple</c> type</param>
        /// <param name="names">The tuple element names</param>
        private static bool SetElementNames(TypeNode type, string[] names)
        {
            if(type.FullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
            {
                TypeNodeList arguments = type.TemplateArguments;

                if(arguments != null)
                {
                    // Use a simple string enumerator.  Only one array is specified even though the elements may
                    // span more than one declaration.  This is the simplest way handle them across all such
                    // declarations.
                    var collection = new System.Collections.Specialized.StringCollection();
                    collection.AddRange(names);
                    type.ElementNames = collection.GetEnumerator();
                    return true;
                }

                return false;
            }

            // Search the types recursively until we find it (i.e. IEnumerable<ValueTuple>)
            if(type.IsGeneric)
            {
                TypeNodeList arguments = type.TemplateArguments;

                if(arguments != null && arguments.Count != 0)
                    foreach(var arg in arguments)
                        if(SetElementNames(arg, names))
                            return true;
            }

            return false;
        }

        /// <summary>
        /// Write out a member reference
        /// </summary>
        /// <param name="member">The member to reference</param>
        public void WriteMemberReference(Member member)
        {
            if(member == null)
                throw new ArgumentNullException(nameof(member));

            writer.WriteStartElement("member");

            Member template = member.GetTemplateMember();

            // !EFW - Change from ComponentOne
            writer.WriteAttributeString("api", namer.GetMemberName(template).TranslateToValidXmlValue());

            if(!member.DeclaringType.IsStructurallyEquivalentTo(template.DeclaringType))
            {
                // !EFW - Change from ComponentOne
                writer.WriteAttributeString("display-api", namer.GetMemberName(member).TranslateToValidXmlValue());
            }

            this.WriteTypeReference(member.DeclaringType);
            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out attributes
        /// </summary>
        /// <param name="attributes">The standard attributes to write</param>
        protected void WriteAttributes(AttributeList attributes)
        {
            var exposed = this.GetExposedAttributes(attributes).ToList();

            // Special cases.  For ref structs and types with required fields or properties, remove the obsolete
            // attribute added by the compiler for versions that don't support them.  However, if the message
            // doesn't contain the "not supported" text, assume it was added by the user and keep it.
            var obsoleteAttr = exposed.FirstOrDefault(a => a.Type.FullName == "System.ObsoleteAttribute");

            if(obsoleteAttr != null && obsoleteAttr.Expressions.Count != 0)
            {
                var exp = obsoleteAttr.Expressions[0];

                if(exp.NodeType == NodeType.Literal)
                {
                    var literal = (Literal)exp;

                    if(literal.Value?.ToString().IndexOf("are not supported in this version of your compiler",
                      StringComparison.Ordinal) != -1)
                    {
                        exposed.Remove(obsoleteAttr);
                    }
                }
            }

            if(exposed.Count != 0)
            {
                writer.WriteStartElement("attributes");

                foreach(var attribute in exposed)
                {
                    writer.WriteStartElement("attribute");

                    this.WriteTypeReference(attribute.Type);

                    foreach(var expression in attribute.Expressions)
                        this.WriteExpression(attribute.Type, expression);

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out an expression
        /// </summary>
        /// <param name="expression">The expression to write</param>
        protected void WriteExpression(TypeNode type, Expression expression)
        {
            if(type == null)
                throw new ArgumentNullException(nameof(type));

            if(expression == null)
                throw new ArgumentNullException(nameof(expression));

            if(expression.NodeType == NodeType.Literal)
            {
                writer.WriteStartElement("argument");

                var literal = (Literal)expression;

                // If the literal is null but the parameter type is a value type, change the type in the
                // literal from Object to the parameter type.  The WriteLiteral() method will then be able
                // to write out the defaultValue element rather than the nullValue element.
                if(literal.Value == null && type.IsValueType && !type.IsNullable)
                    literal = new Literal(null, type);

                this.WriteLiteral(literal);

                writer.WriteEndElement();
            }
            else
                if(expression.NodeType == NodeType.NamedArgument)
                {
                    NamedArgument assignment = (NamedArgument)expression;
                    Literal value = (Literal)assignment.Value;

                    writer.WriteStartElement("assignment");

                    this.WriteStringAttribute("name", assignment.Name.Name);
                    this.WriteLiteral(value);

                    writer.WriteEndElement();
                }
        }

        /// <summary>
        /// Write out generic template parameters
        /// </summary>
        /// <param name="templateParameters">The template parameters to write</param>
        private void WriteGenericParameters(TypeNodeList templateParameters)
        {
            if(templateParameters != null && templateParameters.Count != 0)
            {
                writer.WriteStartElement("templates");

                foreach(var gp in templateParameters)
                    this.WriteGenericParameter(gp);

                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out a generic template parameter
        /// </summary>
        /// <param name="templateParameter">The template parameter to write</param>
        private void WriteGenericParameter(TypeNode templateParameter)
        {
            ITypeParameter itp = (ITypeParameter)templateParameter;

            writer.WriteStartElement("template");

            // !EFW - Change from ComponentOne
            writer.WriteAttributeString("name", templateParameter.Name.Name.TranslateToValidXmlValue());

            // Evaluate constraints
            bool reference = ((itp.TypeParameterFlags & TypeParameterFlags.ReferenceTypeConstraint) > 0),
                value = ((itp.TypeParameterFlags & TypeParameterFlags.ValueTypeConstraint) > 0),
                constructor = ((itp.TypeParameterFlags & TypeParameterFlags.DefaultConstructorConstraint) > 0),
                contravariant = ((itp.TypeParameterFlags & TypeParameterFlags.Contravariant) > 0),
                covariant = ((itp.TypeParameterFlags & TypeParameterFlags.Covariant) > 0);

            InterfaceList interfaces = templateParameter.Interfaces;
            TypeNode parent = templateParameter.BaseType;

            // No need to show inheritance from ValueType if value flag is set
            if(value && parent != null && parent.FullName == "System.ValueType")
                parent = null;

            if(parent != null || interfaces.Count > 0 || reference || value || constructor)
            {
                writer.WriteStartElement("constrained");

                if(reference)
                    this.WriteBooleanAttribute("ref", true);

                if(value)
                    this.WriteBooleanAttribute("value", true);

                if(constructor)
                    this.WriteBooleanAttribute("ctor", true);

                if(parent != null)
                    this.WriteTypeReference(parent);

                this.WriteInterfaces(interfaces);
                writer.WriteEndElement();
            }

            if(covariant || contravariant)
            {
                writer.WriteStartElement("variance");

                if(contravariant)
                    this.WriteBooleanAttribute("contravariant", true);

                if(covariant)
                    this.WriteBooleanAttribute("covariant", true);

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out interfaces
        /// </summary>
        /// <param name="contracts">The interfaces to write out</param>
        private void WriteInterfaces(InterfaceList contracts)
        {
            var implementedContracts = GetExposedInterfaces(contracts);

            if(implementedContracts.Any())
            {
                writer.WriteStartElement("implements");

                this.StartElementCallbacks("implements", implementedContracts);

                foreach(var contract in implementedContracts)
                    this.WriteTypeReference(contract);

                writer.WriteEndElement();

                this.EndElementCallbacks("implements", implementedContracts);
            }
        }

        /// <summary>
        /// Write out a library reference
        /// </summary>
        /// <param name="module">The module to reference</param>
        private void WriteLibraryReference(Module module)
        {
            AssemblyNode assembly = module.ContainingAssembly;

            writer.WriteStartElement("library");

            this.WriteStringAttribute("assembly", assembly.Name);
            this.WriteStringAttribute("module", module.Name);
            this.WriteStringAttribute("kind", module.Kind.ToString());

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out a literal
        /// </summary>
        /// <param name="literal">The literal to write out</param>
        private void WriteLiteral(Literal literal)
        {
            this.WriteLiteral(literal, true);
        }

        /// <summary>
        /// Write out a literal optionally including the type
        /// </summary>
        /// <param name="literal">The literal to write out</param>
        /// <param name="showType">True to show the type, false to not show it</param>
        private void WriteLiteral(Literal literal, bool showType)
        {
            TypeNode type = literal.Type;
            Object value = literal.Value;

            if(showType)
                this.WriteTypeReference(type);

            if(value == null)
                writer.WriteElementString(literal.Type.IsValueType ? "defaultValue" : "nullValue", String.Empty);
            else
            {
                if(type.NodeType == NodeType.EnumNode)
                {
                    EnumNode enumeration = (EnumNode)type;

                    writer.WriteStartElement("enumValue");

                    if(value is ulong longValue)
                        value = unchecked((long)longValue);

                    foreach(var field in GetAppliedFields(enumeration, Convert.ToInt64(value, CultureInfo.InvariantCulture)))
                    {
                        writer.WriteStartElement("field");

                        // !EFW - Change from ComponentOne
                        writer.WriteAttributeString("name", field.Name.Name.TranslateToValidXmlValue());
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }
                else
                {
                    if(type.FullName == "System.Type")
                    {
                        writer.WriteStartElement("typeValue");
                        this.WriteTypeReference((TypeNode)value);
                        writer.WriteEndElement();
                    }
                    else
                    {
                        string text = value.ToString();

                        // If there are invalid XML characters convert them to an escaped equivalent.  This will
                        // work for literals in most of the syntax generators, VB being the exception.
                        if(text.Any(c => c < 0x20 || (c > 0xd7ff && (c < 0xe000 || c > 0xfffd))))
                        {
                            bool isCharOrString = type.FullName == "System.Char" || type.FullName == "System.String";
                            var sb = new StringBuilder(text.Length + 100);

                            foreach(char c in text)
                            {
                                if(c < 0x20 || (c > 0xd7ff && (c < 0xe000 || c > 0xfffd)))
                                {
                                    switch(c)
                                    {
                                        case '\a':
                                            sb.Append("\\a");
                                            break;

                                        case '\b':
                                            sb.Append("\\b");
                                            break;

                                        case '\f':
                                            sb.Append("\\f");
                                            break;

                                        case '\n':
                                            sb.Append("\\n");
                                            break;

                                        case '\r':
                                            sb.Append("\\r");
                                            break;

                                        case '\t':
                                            sb.Append("\\t");
                                            break;

                                        case '\v':
                                            sb.Append("\\v");
                                            break;

                                        default:
                                            if(isCharOrString)
                                                sb.Append("\\x");

                                            sb.AppendFormat("{0:X4}", (int)c);
                                            break;
                                    }
                                }
                                else
                                    sb.Append(c);
                            }

                            text = sb.ToString();
                        }

                        writer.WriteElementString("value", text);
                    }
                }
            }
        }

        /// <summary>
        /// Write out parameters
        /// </summary>
        /// <param name="parameters">The parameters to write out</param>
        private void WriteParameters(ParameterList parameters)
        {
            if(parameters.Count != 0)
            {
                writer.WriteStartElement("parameters");

                foreach(var p in parameters)
                    WriteParameter(p);

                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out a parameter
        /// </summary>
        /// <param name="parameter">The parameter to write out</param>
        private void WriteParameter(Parameter parameter)
        {
            // Certain combinations of platform (.NET 5.0 and .NET Standard 2.x for example) cannot be
            // mixed as the types don't match up correctly between the core assemblies.  In such cases,
            // the assemblies will have to be documented separately.
            if(parameter.Type.Name.Name == "Void")
            {
                // This one may or may not be an error.  It could be a missing reference assembly that was ignored.
                this.MessageLogger(MessageLevel.Warn, "Unexpected parameter type Void.  This may be " +
                    "an issue (missing reference assembly?).  Declaring method: " + parameter.DeclaringMethod.FullName);
            }

            writer.WriteStartElement("parameter");

            // !EFW - Change from ComponentOne
            writer.WriteAttributeString("name", parameter.Name.Name.TranslateToValidXmlValue());

            if(parameter.IsIn)
                this.WriteBooleanAttribute("in", true);

            if(parameter.IsOut)
                this.WriteBooleanAttribute("out", true);

            if(parameter.Attributes != null && parameter.Attributes.Any(a => a != null &&
              a.Type.FullName == "System.ParamArrayAttribute"))
                this.WriteBooleanAttribute("params", true);

            // !EFW - Support optional parameters noted by OptionalAttribute alone (no default value)
            if(parameter.IsOptional)
                this.WriteBooleanAttribute("optional", true);

            this.WriteTypeReference(parameter.Type, null, parameter.Attributes,
                DetermineNullableState(parameter.DeclaringMethod, parameter.Attributes).GetEnumerator());

            if(parameter.IsOptional && parameter.DefaultValue != null)
            {
                this.WriteExpression(parameter.Type, parameter.DefaultValue);
            }

            if(parameter.Attributes != null && parameter.Attributes.Count != 0)
            {
                // Ignore ParamArray as it's handled above
                var paramAttrs = new AttributeList(parameter.Attributes.Where(a => a != null &&
                    a.Type.FullName != "System.ParamArrayAttribute"));

                if(paramAttrs.Count != 0)
                    this.WriteAttributes(paramAttrs);
            }

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out procedure data such as being abstract, virtual, etc.
        /// </summary>
        /// <param name="method">The method for which to write procedure data</param>
        /// <param name="overrides">Member data for the overridden member or null if not an override</param>
        private void WriteProcedureData(Method method, Member overrides)
        {
            writer.WriteStartElement("proceduredata");

            this.WriteBooleanAttribute("abstract", method.IsAbstract, false);
            this.WriteBooleanAttribute("virtual", method.IsVirtual);
            this.WriteBooleanAttribute("final", method.IsFinal, false);
            this.WriteBooleanAttribute("varargs", method.CallingConvention == CallingConventionFlags.VarArg, false);

            // If it's private and has any implemented methods, it's explicitly implemented
            if(method.IsPrivate && method.GetImplementedMethods().Any())
                this.WriteBooleanAttribute("eii", true);

            // Interop attribute data.  In code, these are attributes.  However, the compiler converts the
            // attribute data to type metadata so we don't see them in the regular attribute list.  The metadata
            // for the attributes is too complex to reconstruct so writing the info out as procedure data is much
            // simpler and can be handled by the syntax components as needed.
            if(this.ApiFilter.IncludeAttributes)
            {
                // PInvoke methods may set this too.  Syntax components should omit the attribute if PInvoke
                // information is available.
                if((method.ImplFlags & MethodImplFlags.PreserveSig) != 0)
                    this.WriteBooleanAttribute("preservesig", true);

                if(method.PInvokeModule != null)
                {
                    this.WriteStringAttribute("module", method.PInvokeModule.Name);

                    if(!String.IsNullOrEmpty(method.PInvokeImportName) && method.PInvokeImportName != method.Name.Name)
                        this.WriteStringAttribute("entrypoint", method.PInvokeImportName);

                    switch(method.PInvokeFlags & PInvokeFlags.CallingConvMask)
                    {
                        case PInvokeFlags.CallConvCdecl:
                            this.WriteStringAttribute("callingconvention", "cdecl");
                            break;

                        case PInvokeFlags.CallConvFastcall:
                            this.WriteStringAttribute("callingconvention", "fastcall");
                            break;

                        case PInvokeFlags.CallConvStdcall:
                            this.WriteStringAttribute("callingconvention", "stdcall");
                            break;

                        case PInvokeFlags.CallConvThiscall:
                            this.WriteStringAttribute("callingconvention", "thiscall");
                            break;
                    }

                    if((method.PInvokeFlags & PInvokeFlags.CharSetMask) != 0)
                        switch(method.PInvokeFlags & PInvokeFlags.CharSetMask)
                        {
                            case PInvokeFlags.CharSetAns:
                                this.WriteStringAttribute("charset", "ansi");
                                break;

                            case PInvokeFlags.CharSetUnicode:
                                this.WriteStringAttribute("charset", "unicode");
                                break;

                            case PInvokeFlags.CharSetAuto:
                                this.WriteStringAttribute("charset", "auto");
                                break;
                        }

                    if((method.PInvokeFlags & PInvokeFlags.BestFitDisabled) != 0)
                        this.WriteBooleanAttribute("bestfitmapping", false);

                    if((method.PInvokeFlags & PInvokeFlags.NoMangle) != 0)
                        this.WriteBooleanAttribute("exactspelling", true);

                    if((method.PInvokeFlags & PInvokeFlags.ThrowOnUnmappableCharEnabled) != 0)
                        this.WriteBooleanAttribute("throwonunmappablechar", true);

                    if((method.PInvokeFlags & PInvokeFlags.SupportsLastError) != 0)
                        this.WriteBooleanAttribute("setlasterror", true);
                }
            }

            writer.WriteEndElement();

            if(overrides != null)
            {
                writer.WriteStartElement("overrides");
                this.WriteMemberReference(overrides);
                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write a starting type reference element based on the type node
        /// </summary>
        /// <param name="type">The type for which to write a starting type reference</param>
        /// <param name="elementName">An element name if the type reference is part of a type such as
        /// <c>ValueTuple</c> or null if not.</param>
        /// <param name="nullableStates">An enumerable list of the nullable states for the type's parts</param>
        /// <param name="lastState">The last used nullable state.  If it's common to all parts, there will only
        /// be one entry that applies to all parts.</param>
        /// <param name="addContentProperty">True to add the content property attribute for XAML syntax or
        /// false to omit it.</param>
        private void WriteStartTypeReference(TypeNode type, string elementName = null,
          IEnumerator<NullableState> nullableStates = null, NullableState lastState = NullableState.NotSpecified,
          bool addContentProperty = false)
        {
            NullableState ns = NullableState.NotSpecified;

            if(type.IsValueType)
                ns = NullableState.ValueType;
            else
            {
                if(nullableStates != null && nullableStates.MoveNext())
                    ns = nullableStates.Current;
                else
                    ns = lastState;
            }

            switch(type.NodeType)
            {
                case NodeType.ArrayType:
                    ArrayType array = type as ArrayType;
                    writer.WriteStartElement("arrayOf");
#if DEBUG_SHOW_NULLABLE_STATE
                    if(ns != NullableState.NotSpecified)
                        writer.WriteAttributeString("nullableState", ns.ToString());
#endif
                    if(ns == NullableState.Nullable)
                        writer.WriteAttributeString("nullable", "true");

                    writer.WriteAttributeString("rank", array.Rank.ToString(CultureInfo.InvariantCulture));
                    this.WriteTypeReference(array.ElementType, null, null, nullableStates, ns);
                    break;

                case NodeType.Reference:
                    Reference reference = type as Reference;
                    writer.WriteStartElement("referenceTo");
#if DEBUG_SHOW_NULLABLE_STATE
                    if(ns != NullableState.NotSpecified)
                        writer.WriteAttributeString("nullableState", ns.ToString());
#endif
                    if(ns == NullableState.Nullable)
                        writer.WriteAttributeString("nullable", "true");

                    this.WriteTypeReference(reference.ElementType, null, null, nullableStates, ns);
                    break;

                case NodeType.Pointer:
                    Pointer pointer = type as Pointer;
                    writer.WriteStartElement("pointerTo");
#if DEBUG_SHOW_NULLABLE_STATE
                    if(ns != NullableState.NotSpecified)
                        writer.WriteAttributeString("nullableState", ns.ToString());
#endif
                    if(ns == NullableState.Nullable)
                        writer.WriteAttributeString("nullable", "true");

                    this.WriteTypeReference(pointer.ElementType, null, null, nullableStates, ns);
                    break;

                case NodeType.OptionalModifier:
                    TypeModifier optionalModifierClause = type as TypeModifier;
                    this.WriteStartTypeReference(optionalModifierClause.ModifiedType);
                    writer.WriteStartElement("optionalModifier");
#if DEBUG_SHOW_NULLABLE_STATE
                    if(ns != NullableState.NotSpecified)
                        writer.WriteAttributeString("nullableState", ns.ToString());
#endif
                    if(ns == NullableState.Nullable)
                        writer.WriteAttributeString("nullable", "true");

                    this.WriteTypeReference(optionalModifierClause.Modifier, null, null, nullableStates, ns);
                    writer.WriteEndElement();
                    break;

                case NodeType.RequiredModifier:
                    TypeModifier requiredModifierClause = type as TypeModifier;
                    this.WriteStartTypeReference(requiredModifierClause.ModifiedType);
                    writer.WriteStartElement("requiredModifier");
#if DEBUG_SHOW_NULLABLE_STATE
                    if(ns != NullableState.NotSpecified)
                        writer.WriteAttributeString("nullableState", ns.ToString());
#endif
                    if(ns == NullableState.Nullable)
                        writer.WriteAttributeString("nullable", "true");

                    this.WriteTypeReference(requiredModifierClause.Modifier, null, null, nullableStates, ns);
                    writer.WriteEndElement();
                    break;

                default:
                    if(type.IsTemplateParameter)
                    {
                        ITypeParameter gtp = (ITypeParameter)type;
                        writer.WriteStartElement("template");

                        // !EFW - Change from ComponentOne
                        writer.WriteAttributeString("name", type.Name.Name.TranslateToValidXmlValue());
                        writer.WriteAttributeString("index", gtp.ParameterListIndex.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("api", namer.GetApiName(gtp.DeclaringMember).TranslateToValidXmlValue());
                    }
                    else
                    {
                        writer.WriteStartElement("type");
#if DEBUG_SHOW_NULLABLE_STATE
                        if(ns != NullableState.NotSpecified)
                            writer.WriteAttributeString("nullableState", ns.ToString());
#endif
                        if(ns == NullableState.Nullable)
                            writer.WriteAttributeString("nullable", "true");

                        if(!String.IsNullOrWhiteSpace(elementName))
                            writer.WriteAttributeString("elementName", elementName);

                        if(type.IsGeneric)
                        {
                            TypeNode template = type.GetTemplateType();

                            // !EFW - Change from ComponentOne
                            writer.WriteAttributeString("api", namer.GetTypeName(template).TranslateToValidXmlValue());
                            this.WriteBooleanAttribute("ref", !template.IsValueType);

                            // Record specialization							
                            TypeNodeList arguments = type.TemplateArguments;

                            if(arguments != null && arguments.Count > 0)
                            {
                                writer.WriteStartElement("specialization");

                                foreach(var arg in arguments)
                                {
                                    string nextElementName = null;

                                    // If we've got element names, pull the next one off the list and use it
                                    if(type.ElementNames != null && type.ElementNames.MoveNext())
                                        nextElementName = type.ElementNames.Current;

                                    this.WriteTypeReference(arg, nextElementName, null, nullableStates, ns);
                                }

                                writer.WriteEndElement();
                            }
                        }
                        else
                        {
                            // !EFW - Change from ComponentOne
                            writer.WriteAttributeString("api", namer.GetTypeName(type).TranslateToValidXmlValue());
                            this.WriteBooleanAttribute("ref", !type.IsValueType);
                        }

                        // Record outer types because they may be specialized and otherwise that information
                        // is lost.
                        if(type.DeclaringType != null)
                            this.WriteTypeReference(type.DeclaringType);
                    }

                    if(addContentProperty)
                    {
                        var contentProperty = type.Attributes.FirstOrDefault(a => a.Type.Name.Name == "ContentPropertyAttribute" &&
                            a.Expressions.Count == 1);

                        if(contentProperty != null && contentProperty.Expressions[0] is Literal l)
                            this.WriteStringAttribute("contentProperty", "P:" + type.FullName + "." + l.Value.ToString());
                    }
                    break;
            }
        }

        /// <summary>
        /// Write out type containers
        /// </summary>
        /// <param name="type">The type for which to write out containers</param>
        private void WriteTypeContainers(TypeNode type)
        {
            writer.WriteStartElement("containers");

            this.WriteLibraryReference(type.DeclaringModule);
            this.WriteNamespaceReference(type.GetNamespace());

            // For nested types, record outer types
            TypeNode outer = type.DeclaringType;

            if(outer != null)
                this.WriteTypeReference(outer);

            writer.WriteEndElement();
        }

        /// <summary>
        /// Write out a field or return value type
        /// </summary>
        /// <param name="type">The return value type</param>
        /// <param name="attributes">The return value attributes if any</param>
        /// <param name="nullableStates">The nullable states for the return value type</param>
        private void WriteReturnValue(TypeNode type, AttributeList attributes, IEnumerator<NullableState> nullableStates = null)
        {
            if(type.FullName != "System.Void")
            {
                writer.WriteStartElement("returns");
                this.WriteTypeReference(type, null, attributes, nullableStates);
                writer.WriteEndElement();
            }
        }

        /// <summary>
        /// Write out a Boolean attribute value
        /// </summary>
        /// <param name="attribute">The attribute name</param>
        /// <param name="value">The attribute value</param>
        private void WriteBooleanAttribute(string attribute, bool value)
        {
            writer.WriteAttributeString(attribute, value ? "true" : "false");
        }

        /// <summary>
        /// Write out a Boolean attribute value if it does not match the given default value
        /// </summary>
        /// <param name="attribute">The attribute name</param>
        /// <param name="value">The attribute value</param>
        /// <param name="defaultValue">The default attribute value</param>
        private void WriteBooleanAttribute(string attribute, bool value, bool defaultValue)
        {
            if(value != defaultValue)
                writer.WriteAttributeString(attribute, value ? "true" : "false");
        }

        /// <summary>
        /// Write out a string attribute value
        /// </summary>
        /// <param name="attribute">The attribute name</param>
        /// <param name="value">The attribute value</param>
        /// <remarks>The value is checked for invalid XML characters.  Any that are found are removed first.</remarks>
        private void WriteStringAttribute(string attribute, string value)
        {
            // !EFW - Change from ComponentOne
            writer.WriteAttributeString(attribute, value.TranslateToValidXmlValue());
        }

        /// <summary>
        /// Determine the nullable state of a member based on its type and the presence of a Nullable or
        /// NullableContext attribute.
        /// </summary>
        /// <param name="parent">The parent member or type to use for context</param>
        /// <param name="attributes">The attributes to search</param>
        /// <returns>An enumerable list of the nullable states</returns>
        private static IEnumerable<NullableState> DetermineNullableState(Member parent, AttributeList attributes)
        {
            Literal literal = null;
            byte[] states = [];
            var nullableStates = new List<NullableState>();

            if(attributes != null && attributes.Count != 0)
            {
                // For reference types, there may be a Nullable or NullableContext attribute that
                // defines the nullable state.
                var nullable = attributes.FirstOrDefault(a => a.Type.Name.Name == "NullableAttribute");

                // The expression will be a single byte if the state is common to all parts of the
                // type.  For arrays and generics with differing states, it will be a byte[] that defines
                // the nullable state of each part.  The states are as defined in the NullableState
                // enumeration (0 = Oblivious, 1 = Non-nullable, and 2 = Nullable).  For example:
                //
                // [Nullable({ 2, 1, 2, 1 })]
                // Dictionary<string, string[]?>? x;
                //
                // Element 0 is for the type overall (a nullable dictionary), element 1 is for the key
                // (a non-nullable string), element 2 is for the nullable array, and element 3 is for the
                // array type (non-nullable strings).
                // 
                // As shown above, if another array or generic is nested as a type parameter, the pattern
                // repeats for the subelements (the first is for the type overall followed by its parts).
                if(nullable?.Expressions?.Count == 1 && nullable.Expressions[0] is Literal na)
                    literal = na;
                else
                {
                    var context = attributes.FirstOrDefault(a => a.Type.Name.Name == "NullableContextAttribute");

                    if(context?.Expressions?.Count == 1 && context.Expressions[0] is Literal nc)
                        literal = nc;
                }

                if(literal != null)
                {
                    if(literal.Value is byte value)
                        states = [value];
                    else
                    {
                        if(literal.Value is byte[] values)
                            states = values;
                    }
                }
            }

            if(states.Length == 0)
            {
                if(parent is Method method)
                    return DetermineNullableState(method.DeclaringType, method.Attributes);

                if(parent is TypeNode type)
                    nullableStates.Add(type.NullableContext);
                else
                    nullableStates.Add(NullableState.NotSpecified);
            }
            else
                foreach(byte s in states)
                {
                    if(Enum.IsDefined(typeof(NullableState), (int)s))
                        nullableStates.Add((NullableState)s);
                    else
                        nullableStates.Add(NullableState.Oblivious);
                }

            return nullableStates;
        }
        #endregion

        #region Merge duplicate type and member reflection information
        //=====================================================================

        /// <summary>
        /// This is used to merge duplicate type and member information in a reflection data file into single
        /// entries containing the necessary element and library information.
        /// </summary>
        /// <param name="reflectionDataFile">The source reflection data file</param>
        /// <remarks>This will merge duplicate type and member information in the given reflection data file.
        /// This process is easier after the file is created rather than trying to do it when the types are being
        /// visited and written to it.  The merged data is saved back to the same filename.</remarks>
        /// <returns>A tuple containing the number of merged types and members</returns>
        public (int MergedTypes, int MergedMembers) MergeDuplicateReflectionData(string reflectionDataFile)
        {
            var duplicateTypesAndMembers = new Dictionary<string, List<XElement>>();
            var duplicatesSeen = new HashSet<string>();
            int mergedTypes = 0, mergedMembers = 0;
            bool hasDuplicates = false;

            writer.Close();

            // Make a first pass to find duplicate types and members
            using(XmlReader reader = XmlReader.Create(reflectionDataFile, new XmlReaderSettings {
              IgnoreWhitespace = true, CloseInput = true }))
            {
                reader.ReadToFollowing("api");

                while(!reader.EOF)
                {
                    string id = reader.GetAttribute("id");

                    if(duplicateTypesAndMembers.TryGetValue(id, out List<XElement> duplicates))
                    {
                        if(duplicates == null)
                        {
                            duplicates = [];
                            duplicateTypesAndMembers[id] = duplicates;
                        }

                        duplicates.Add((XElement)XNode.ReadFrom(reader));
                        hasDuplicates = true;

                        if(reader.NodeType == XmlNodeType.EndElement)
                            reader.ReadToFollowing("api");
                    }
                    else
                    {
                        duplicateTypesAndMembers.Add(id, null);
                        reader.ReadToFollowing("api");
                    }
                }
            }

            if(!hasDuplicates)
                return (0, 0);

            string mergedReflectionDataFile = Path.Combine(Path.GetDirectoryName(reflectionDataFile),
                Guid.NewGuid().ToString() + ".xml");

            // Clone the file and merge the duplicates.
            // Don't use a simplified using statement here.  We need to ensure the reader and writer are disposed
            // of before deleting and moving the files below.
            using(XmlReader reader = XmlReader.Create(reflectionDataFile, new XmlReaderSettings {
              IgnoreWhitespace = true, CloseInput = true }))
            using(XmlWriter writer = XmlWriter.Create(mergedReflectionDataFile, new XmlWriterSettings {
              Indent = true, CloseOutput = true }))
            {
                writer.WriteStartDocument();
                reader.Read();

                while(!reader.EOF)
                {
                    if(reader.NodeType != XmlNodeType.Element)
                    {
                        reader.Read();
                        continue;
                    }

                    switch(reader.Name)
                    {
                        case "apis":
                        case "reflection":
                            writer.WriteStartElement(reader.Name);
                            reader.Read();
                            break;

                        case "api":
                            string id = reader.GetAttribute("id");

                            // Merge duplicate types and members
                            var duplicates = duplicateTypesAndMembers[id];

                            if(duplicates == null)
                                writer.WriteNode(reader.ReadSubtree(), true);
                            else
                            {
                                var apiNode = (XElement)XNode.ReadFrom(reader);

                                // Add the first one, skip the duplicates
                                if(!duplicatesSeen.Contains(id))
                                {
                                    // Merge library info
                                    var library = apiNode.Element("containers").Element("library");
                                    string assemblyName = library.Attribute("assembly").Value;

                                    // There can be duplicate assembly names so we only add the first of each
                                    library.AddAfterSelf(duplicates.Select(
                                        d => d.Element("containers").Element("library")).GroupBy(
                                            l => l.Attribute("assembly").Value).Where(
                                                g => g.Key != assemblyName).Select(g => g.First()));

                                    if(id[0] == 'T')
                                    {
                                        var thisNodesElements = apiNode.Element("elements") ?? new XElement("elements");
                                        var allElements = duplicates.SelectMany(d => d.Descendants("element")).Concat(
                                            thisNodesElements.Descendants("element"));
                                        int count = duplicates.Count + 1;

                                        // Merge element info for types.  First, see which ones don't appear in
                                        // all copies of the type.
                                        var missingMembers = allElements.Select(
                                            el => (el.Attribute("api").Value, el)).GroupBy(
                                                m => m.Value).Where(g => g.Count() != count);

                                        // For those that don't but they do exist in the node we are keeping, add
                                        // library info to those elements.  If they don't exist in the node we are
                                        // keeping, add a new element to the node with the library info.
                                        foreach(var g in missingMembers)
                                        {
                                            var memberElement = g.FirstOrDefault(m => m.el.Parent.Parent == apiNode).el;

                                            if(memberElement == null)
                                            {
                                                memberElement = new XElement(g.First().el);
                                                thisNodesElements.Add(memberElement);
                                            }

                                            var libraries = new XElement("libraries");

                                            memberElement.Add(libraries);

                                            foreach(var lib in g.Select(m => m.el.Parent.Parent.Element(
                                              "containers").Element("library")).OrderBy(l => l.Attribute("assembly").Value))
                                            {
                                                libraries.Add(new XElement(lib));
                                            }
                                        }

                                        mergedTypes += count;
                                    }
                                    else
                                        mergedMembers++;

                                    duplicatesSeen.Add(id);
                                    apiNode.WriteTo(writer);
                                }
                            }
                            break;

                        default:
                            writer.WriteNode(reader.ReadSubtree(), true);
                            break;
                    }
                }

                writer.WriteEndDocument();
            }

            File.Delete(reflectionDataFile);
            File.Move(mergedReflectionDataFile, reflectionDataFile);

            return (mergedTypes, mergedMembers);
        }
        #endregion
    }
}

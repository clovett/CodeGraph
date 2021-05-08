using Microsoft.VisualStudio.GraphModel;
using Microsoft.VisualStudio.GraphModel.Schemas;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;

namespace CodeGraph
{
    public class AssemblyGraph
    {
        // TODO: add field level detail when requested.
        // TODO: add option to show type & method dependencies into external assemblies

        public AssemblyGraph(string fileName)
        {
            this.fileName = fileName;
        }

        private string fileName;
        public bool AssemblyDependencies;
        public bool NamespaceDependencies;
        public bool TypeDependencies;
        public bool MethodCallDependencies;
        public bool FieldDependencies;
        public bool PrivateDependencies;
        Graph graph;

        public Graph Generate(Graph graph)
        {
            this.graph = graph;
            using (var scope = graph.BeginUpdate(Guid.NewGuid(), "", UndoOption.Disable))
            {
                var id = Path.GetFileNameWithoutExtension(fileName);
                var node = graph.Nodes.GetOrCreate(fileName, id, CodeNodeCategories.Assembly);

                if (AssemblyDependencies)
                {
                    AddAssemblyDependencies(node, fileName, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }
                else
                {
                    var a = AssemblyDefinition.ReadAssembly(fileName);
                    foreach (var t in a.MainModule.GetTypes())
                    {
                        VisitType(node, t);
                    }
                }
                scope.Complete();
            }

            return graph;
        }

        private void AddAssemblyDependencies(GraphNode node, string filename, HashSet<string> loaded)
        {
            var dir = Path.GetDirectoryName(filename);
            var id = Path.GetFileNameWithoutExtension(filename);
            if (loaded.Contains(id))
            {
                return;
            }
            loaded.Add(id);
            Graph graph = node.Owner;
            var a = AssemblyDefinition.ReadAssembly(filename);
            foreach (var ar in a.MainModule.AssemblyReferences)
            {
                id = ar.Name;
                var refNode = graph.Nodes.GetOrCreate(id, id, CodeNodeCategories.Assembly);
                graph.Links.GetOrCreate(node, refNode);
                var localFile = Path.Combine(dir, id + ".dll");
                if (File.Exists(localFile))
                {
                    AddAssemblyDependencies(refNode, localFile, loaded);
                }
            }
        }

        void VisitType(GraphNode assembly, TypeDefinition t)
        {
            var node = GetOrCreateTypeNode(assembly, t);

            // todo: deal with nested types...
            if (t.IsPublic || PrivateDependencies)
            {
                foreach (var method in t.Methods)
                {
                    VisitMethod(assembly, node, t, method);
                }

                foreach (var prop in t.Properties)
                {
                    VisitProperty(assembly, node, t, prop);
                }

                if (FieldDependencies)
                {
                    foreach (var f in t.Fields)
                    {
                        VisitField(assembly, node, t, f);
                    }
                }
            }
        }

        GraphCategory GetGraphCategory(TypeReference t)
        {
            TypeDefinition d = t.Resolve();
            if (d == null)
            {
                return null;
            }
            if (d.IsEnum)
            {
                return CodeNodeCategories.Enum;
            }
            else if (d.IsClass)
            {
                return CodeNodeCategories.Class;
            }
            else if (d.IsInterface)
            {
                return CodeNodeCategories.Interface;
            }
            else if (t.IsValueType)
            {
                return CodeNodeCategories.Struct;
            }
            else
            {
                throw new Exception("???");
            }
        }

        private void MakeGroup(GraphNode node)
        {
            if (!node.IsGroup)
            {
                node.IsGroup = true;
                node.SetValue<GraphGroupStyle>(GraphCommonSchema.Group, GraphGroupStyle.Expanded);
            }
        }

        Dictionary<string, GraphNode> nspaceMap = new Dictionary<string, GraphNode>();

        private GraphNode GetOrCreateNamespaceNode(GraphNode assembly, string name)
        {
            string[] parts = name.Split('.');
            string nspace = string.Join(".", parts, 0, parts.Length - 1);
            if (nspaceMap.TryGetValue(nspace, out GraphNode node))
            {
                return node;
            }

            GraphNode parent = assembly;            
            for (int i = 0; i < parts.Length - 1; i++)
            {
                nspace = string.Join(".", parts, 0, i + 1);
                var label = parts[i];
                GraphNode child = this.graph.Nodes.GetOrCreate(nspace, label, CodeNodeCategories.Namespace);
                nspaceMap[nspace] = child;
                this.graph.Links.GetOrCreate(parent, child, null, GraphCommonSchema.Contains);
                MakeGroup(parent);
                parent = child;
            }
            return parent;
        }

        private GraphNode GetOrCreateTypeNode(GraphNode assembly, TypeDefinition t1)
        {
            if (t1.IsArray)
            {
                t1 = t1.GetElementType().Resolve();
            }

            if ((!t1.IsNested && t1.IsPublic) || t1.IsNestedPublic || PrivateDependencies)
            {
                if (t1.Name != "Void" && t1.Name != "String" && !t1.IsPrimitive && t1.Name != "<Module>" && !t1.Name.StartsWith("<"))
                {
                    GraphNode parent = assembly;
                    if (t1.DeclaringType == null)
                    {
                        if (NamespaceDependencies && t1.FullName.Contains("."))
                        {
                            parent = GetOrCreateNamespaceNode(assembly, t1.FullName);
                        }
                    }
                    else
                    {
                        // walk up nested type hierarchy.
                        parent = GetOrCreateTypeNode(assembly, t1.DeclaringType);
                    }

                    GraphNode n1 = this.graph.Nodes.GetOrCreate(t1.FullName, t1.Name, GetGraphCategory(t1));
                    this.graph.Links.GetOrCreate(parent, n1, null, GraphCommonSchema.Contains);
                    MakeGroup(parent);
                    return n1;
                }
            }
            return null;
        }

        private void GetOrCreateTypeReference(GraphNode n1, GraphNode n2)
        {
            if (n1 != null && n2 != null)
            {
                this.graph.Links.GetOrCreate(n1, n2, null, CodeLinkCategories.References);
            }
        }

        private void VisitMethod(GraphNode parent, GraphNode typeNode, TypeDefinition type, MethodDefinition method)
        {
            if (method.ReturnType != null )
            {
                if (method.ReturnType.IsGenericInstance)
                {
                    VisitGenericParameters(parent, typeNode, type, method.ReturnType as GenericInstanceType);
                }

                TypeDefinition rdef = method.ReturnType.Resolve();
                if (rdef != null && rdef.Module == type.Module)
                {
                    GraphNode n2 = rdef == type ? typeNode : GetOrCreateTypeNode(parent, rdef);
                    if (n2 != null)
                    {
                        GetOrCreateTypeReference(typeNode, n2);
                    }
                }
                else
                {
                    // todo: graph external type dependencies
                }
            }
            if (method.HasParameters)
            {
                foreach(var p in method.Parameters)
                {
                    if (p.ParameterType != null)
                    {
                        if (p.ParameterType.IsGenericInstance)
                        {
                            VisitGenericParameters(parent, typeNode, type, p.ParameterType as GenericInstanceType);
                        }

                        TypeDefinition rdef = p.ParameterType.Resolve();
                        if (rdef != null && rdef.Module == type.Module)
                        {
                            GraphNode n2 = rdef == type ? typeNode : GetOrCreateTypeNode(parent, rdef);
                            if (n2 != null)
                            {
                                GetOrCreateTypeReference(typeNode, n2);
                            }
                        }
                        else
                        {
                            // todo: graph external type dependencies
                        }
                    }
                }
            }

            if (MethodCallDependencies)
            {
                GraphNode m1 = this.graph.Nodes.GetOrCreate(method.FullName, method.Name, CodeNodeCategories.Method);
                this.graph.Links.GetOrCreate(typeNode, m1, null, GraphCommonSchema.Contains);
                MakeGroup(typeNode);

                var ilreader = method.Body.GetILProcessor();
                foreach (var i in ilreader.Body.Instructions)
                {
                    if (i.OpCode == Mono.Cecil.Cil.OpCodes.Call || i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
                    {
                        if (i.Operand is MethodReference mr)
                        {
                            MethodDefinition md = mr.Resolve();
                            if (md != null && md.Module == type.Module)
                            {
                                GraphNode t2 = (type == md.DeclaringType) ?  typeNode : GetOrCreateTypeNode(parent, md.DeclaringType);
                                if (t2 != null)
                                {
                                    GraphNode m2 = this.graph.Nodes.GetOrCreate(md.FullName, md.Name, CodeNodeCategories.Method);
                                    this.graph.Links.GetOrCreate(t2, m2, null, GraphCommonSchema.Contains);
                                    MakeGroup(t2);
                                    this.graph.Links.GetOrCreate(m1, m2, null, CodeLinkCategories.Calls);
                                }
                            }
                            else
                            {
                                // todo: handle external dependencies
                            }
                        }
                    }
                }
            }
        }

        private void VisitProperty(GraphNode parent, GraphNode typeNode, TypeDefinition t, PropertyDefinition prop)
        {
            if (prop.PropertyType.IsGenericInstance)
            {
                VisitGenericParameters(parent, typeNode, t, prop.PropertyType as GenericInstanceType);
            }

            TypeDefinition t2 = prop.PropertyType.Resolve();
            if (t2 != null)
            {
                GraphNode n2 = t2 == t ? typeNode : GetOrCreateTypeNode(parent, t2);
                if (n2 != null && t2.Module == t.Module)
                {
                    GetOrCreateTypeReference(typeNode, n2);
                }
                else
                {
                    // todo: handle external dependencies
                }
            }
        }

        private void VisitGenericParameters(GraphNode parent, GraphNode typeNode, TypeDefinition t, GenericInstanceType g)
        {
            foreach (var gp in g.GenericArguments)
            {
                // these are also type references!
                TypeDefinition t2 = gp.Resolve();
                if (t2 != null && t2.Module == t.Module)
                {
                    GraphNode n2 = t2 == t ? typeNode : GetOrCreateTypeNode(parent, t2);
                    if (n2 != null)
                    {
                        GetOrCreateTypeReference(typeNode, n2);
                    }
                }
                else
                {
                    // todo: handle external dependencies
                }
            }
        }


        private void VisitField(GraphNode parent, GraphNode typeNode, TypeDefinition t, FieldDefinition f)
        {
            if (f.FieldType.IsGenericInstance)
            {
                VisitGenericParameters(parent, typeNode, t, f.FieldType as GenericInstanceType);
            }

            TypeDefinition t2 = f.FieldType.Resolve();
            if (t2 != null && t2.Module == t.Module)
            {
                GraphNode n2 = t2 == t ? typeNode : GetOrCreateTypeNode(parent, t2);
                if (n2 != null)
                {
                    GetOrCreateTypeReference(typeNode, n2);
                }
            }
            else
            {
                // todo: handle external dependencies
            }
        }
    }
}

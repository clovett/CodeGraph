using Microsoft.VisualStudio.GraphModel;
using Microsoft.VisualStudio.GraphModel.Schemas;
using System;
using System.Collections.Generic;

namespace CodeGraph
{
    public class Program
    {
        string fileName = null;
        List<AssemblyGraph> graphs = new List<AssemblyGraph>();

        static void Main(string[] args)
        {
            Program p = new Program();
            if (p.ParseCommandLine(args))
            {
                p.Run();
            }
            else
            {
                PrintUsage();
            }
        }

        private void Run()
        {
            Graph graph = new Graph();
            graph.AddSchema(CodeSchema.Schema);
            foreach(var item in graphs)
            {
                item.Generate(graph);
            }

            if (string.IsNullOrEmpty(fileName))
            {
                graph.Save(Console.OpenStandardOutput());
            }
            else
            {
                graph.Save(fileName);
                Console.WriteLine("Saved " + fileName);
            }
        }

        private bool ParseCommandLine(string[] args)
        {
            bool assemblyDependencies = false;
            bool namespaceDependencies = false;
            bool typeDependencies = false;
            bool methodCallDependencies = false;
            bool fieldDependencies = false;
            bool privateDependencies = false;
            List<string> assemblies = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg[0] == '-')
                {
                    string option = arg.Trim('-').ToLower();
                    switch (option)
                    {
                        case "i":
                            if (i + 1 < args.Length)
                            {
                                assemblies.Add(args[++i]);
                            }
                            else
                            {
                                Console.WriteLine("### Error missing assembly name");
                                return false;
                            }
                            break;
                        case "a":
                            assemblyDependencies = true;
                            break;
                        case "n":
                            namespaceDependencies = true;
                            break;
                        case "t":
                            typeDependencies = true;
                            break;
                        case "m":
                            methodCallDependencies = true;
                            break;
                        case "f":
                            fieldDependencies = true;
                            break;
                        case "p":
                            privateDependencies = true;
                            break;
                        
                        case "?":
                        case "h":
                        case "help":
                            return false;
                    }
                }
                else if (fileName == null)
                {
                    fileName = arg;
                }
                else
                {
                    Console.WriteLine("### Too many arguments");
                    return false;
                }
            }
            if (assemblies.Count == 0)
            {
                Console.WriteLine("### Must provide some assembly inputs wiht the -i option");
                return false;
            }
            foreach (var name in assemblies)
            {
                graphs.Add(new AssemblyGraph(name)
                {
                    AssemblyDependencies = assemblyDependencies,
                    NamespaceDependencies = namespaceDependencies,
                    TypeDependencies = typeDependencies,
                    MethodCallDependencies = methodCallDependencies,
                    FieldDependencies = fieldDependencies,
                    PrivateDependencies = privateDependencies
                });
            }
            return true;
        }


        static void PrintUsage()
        {
            Console.WriteLine("Usage: CodeGraph [options] filename.dgml");
            Console.WriteLine();
            Console.WriteLine("Generates various DGML diagrams from given input assemblies:");
            Console.WriteLine("Options:");
            Console.WriteLine("   -i  assemblypath");
            Console.WriteLine("   -a  add assembly dependency graph");
            Console.WriteLine("   -n  add namespace dependency graph");
            Console.WriteLine("   -t  add type dependency graph");
            Console.WriteLine("   -m  add method call dependencies");
            Console.WriteLine("   -f  field level dependencies");
            Console.WriteLine("   -p  include dependencies from private members");
        }
    }
}

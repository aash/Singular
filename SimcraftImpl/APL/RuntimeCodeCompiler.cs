using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Markup;
using Microsoft.CSharp;
using Styx.Common;
using System.Windows.Media;

namespace Simcraft.APL
{
    public static class RuntimeCodeCompiler
    {
        private static volatile Dictionary<string, Assembly> cache = new Dictionary<string, Assembly>();
        private static object syncRoot = new object();
        static Dictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>();
        static RuntimeCodeCompiler()
        {
            AppDomain.CurrentDomain.AssemblyLoad += (sender, e) =>
            {
                assemblies[e.LoadedAssembly.FullName] = e.LoadedAssembly;
            };
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                Assembly assembly = null;
                assemblies.TryGetValue(e.Name, out assembly);
                return assembly;
            };

        }


        public static Assembly CompileCode(string code)
        {

            var provider = new CSharpCodeProvider();
#pragma warning disable 618
            var compiler = provider.CreateCompiler();
#pragma warning restore 618
            var compilerparams = new CompilerParameters
            {
                GenerateExecutable = false,
                GenerateInMemory = false
            };

            var asmCashe = Path.Combine(Utilities.AssemblyDirectory, "CompiledAssemblies");

            var asms = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a =>
                {
                    try { return a.Location; }
                    catch (Exception) { return null; }
                })
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var nonCached = asms.Where(loc => !loc.StartsWith(asmCashe));

            nonCached.ForEach(asm =>
            {
                compilerparams.ReferencedAssemblies.Add(asm);
            });

            // we can not just put all the referenced assemblies because they may define
            // the same type, so we need to filter all assembles from compiler cache
            // CompiledAssemblies\*.dll
            // and pick latest builds
            var cached = asms.Where(loc => loc.StartsWith(asmCashe));
            var uniqueLatest = cached
                .Select(a => new
                {
                    prefix = Regex.Replace(a, "_\\d+\\.dll$", ""),
                    key = File.GetCreationTime(a),
                    location = a
                })
                .GroupBy(a => a.prefix)
                .Select(g => g.OrderBy(a => a.key).First().location);

            uniqueLatest.ForEach(asm =>
            {
                compilerparams.ReferencedAssemblies.Add(asm);
            });

            var results =
               compiler.CompileAssemblyFromSource(compilerparams, code);
            if (results.Errors.HasErrors)
            {
                var errors = new StringBuilder("Compiler Errors :\r\n");
                foreach (CompilerError error in results.Errors)
                {
                    SimcraftImpl.Write(String.Format("Line {0},{1}\t: {2} - line {3}\n",error.Line, error.Column, error.ErrorText, error), default(Color), LogLevel.Normal);
                    //Console.WriteLine(fullExpression.Split(new string[] { Environment.NewLine }, StringSplitOptions.None)[error.Line-1]);
                    //errors.AppendFormat("Line {0},{1}\t: {2} - line {3}\n",
                           //error.Line, error.Column, error.ErrorText, error, fullExpression.Split(new string[] { Environment.NewLine }, StringSplitOptions.None)[error.Line]);
                }
                throw new Exception(errors.ToString());
            }
            AppDomain.CurrentDomain.Load(results.CompiledAssembly.GetName());
            return results.CompiledAssembly;
        }

        public static Assembly CompileCodeOrGetFromCache(string code, string key)
        {
            bool exists = cache.ContainsKey(key);

            if (!exists)
            {

                lock (syncRoot)
                {
                    exists = cache.ContainsKey(key);

                    if (!exists)
                    {
                        cache.Add(key, CompileCode(code));
                    }
                }
            }

            return cache[key];
        }


    }
}

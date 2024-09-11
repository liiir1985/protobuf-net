using Google.Protobuf.Reflection;
using ProtoBuf;
using ProtoBuf.Reflection;
using RazorLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace protogen.CodeGenerators
{  
    public class RazorCodeGenerator : CodeGenerator
    {
        public delegate string TypeGenerationDelegate(DescriptorProto proto, int ident = 0);
        public delegate string EnumGenerationDelegate(EnumDescriptorProto proto, int ident = 0);
        public delegate string GetCodeNamespaceDelegate(FileDescriptorProto proto);
        public class ModelBase
        {
            public TypeGenerationDelegate GenerateType { get; set; }
            public EnumGenerationDelegate GenerateEnum { get; set; }

            public string ToUpperCamel(string val)
            {
                if (!string.IsNullOrEmpty(val))
                    return val.Substring(0, 1).ToUpper() + val.Substring(1);
                return val;
            }
            public string ToSmallCamel(string val)
            {
                if (!string.IsNullOrEmpty(val))
                    return val.Substring(0, 1).ToLower() + val.Substring(1);
                return val;
            }
        }
        public class SingleFileModel : ModelBase
        {
            public FileDescriptorProto File { get; set; }
        }
        public class GlobalFileModel : ModelBase
        {
            public FileDescriptorSet Files { get; set; }
        }
        public class TypeModel : ModelBase
        {
            public DescriptorProto TypeInfo { get; set; }

            public FileDescriptorProto FileInfo { get; set; }

            public GetCodeNamespaceDelegate GetCodeNamespace { get; set; }
            public int CurrentIdent { get; set; }

            WireType GetWireTypeByType(FieldDescriptorProto.Type type)
            {
                WireType wireType = WireType.Varint;
                switch (type)
                {
                    case FieldDescriptorProto.Type.TypeMessage:                        
                    case FieldDescriptorProto.Type.TypeBytes:
                    case FieldDescriptorProto.Type.TypeString:
                        wireType = WireType.String;
                        break;
                    case FieldDescriptorProto.Type.TypeFloat:
                    case FieldDescriptorProto.Type.TypeFixed32:
                    case FieldDescriptorProto.Type.TypeSfixed32:
                        wireType = WireType.Fixed32;
                        break;
                    case FieldDescriptorProto.Type.TypeDouble:
                    case FieldDescriptorProto.Type.TypeFixed64:
                    case FieldDescriptorProto.Type.TypeSfixed64:
                        wireType = WireType.Fixed64;
                        break;
                }
                return wireType;
            }
            public string MakeTag(FieldDescriptorProto proto, bool pack = true)
            {
                int fieldNumber = proto.Number;
                WireType wireType;
                if (proto.label == FieldDescriptorProto.Label.LabelRepeated)
                {
                    wireType = GetWireTypeByType(proto.type);
                    switch (wireType)
                    {
                        case WireType.String:
                            break;
                        default:
                            if (pack)
                            {
                                //packed repeated field
                                wireType = WireType.String;
                            }
                            break;
                    }
                }
                else
                {
                    wireType = GetWireTypeByType(proto.type);
                }
                return ((uint)(fieldNumber << 3) | (uint)wireType).ToString();
            }

            public bool IsMap(DescriptorProto proto)
            {
                if (proto.FullyQualifiedName.StartsWith($"{GetFullQualifiedName(proto.Parent)}.Map") && proto.Name.EndsWith("Entry"))
                    return true;
                else
                    return false;
            }

            public bool IsFieldMap(FieldDescriptorProto proto)
            {
                if (proto.ResolvedType != null && proto.TypeName.StartsWith($"{GetFullQualifiedName(proto.ResolvedType.Parent)}.Map") && proto.TypeName.EndsWith("Entry"))
                    return true;
                else
                    return false;
            }

            public bool IsRepeated(FieldDescriptorProto proto)
            {
                return proto.label == FieldDescriptorProto.Label.LabelRepeated;
            }
            public bool IsRequired(FieldDescriptorProto proto)
            {
                return proto.label == FieldDescriptorProto.Label.LabelRequired;
            }
            public bool IsOptional(FieldDescriptorProto proto)
            {
                return proto.label == FieldDescriptorProto.Label.LabelOptional;
            }
            public FieldDescriptorProto GetMapFieldType(FieldDescriptorProto proto, bool isKey)
            {
                DescriptorProto type = (DescriptorProto)proto.ResolvedType;
                return isKey ? type.Fields[0] : type.Fields[1];
            }
            public string GetMessageTypeName(FieldDescriptorProto proto)
            {
                (string package, string name) = GetPackageNameAndNamespace(proto.ResolvedType);
                string res;
                if (package == ".")
                {
                    if (proto.TypeName.StartsWith("."))
                        res = proto.TypeName.Substring(1);
                    else
                        res = proto.TypeName;
                }
                else
                    res = proto.TypeName.Replace(package, name);
                if (res.StartsWith("."))
                    res = res.Substring(1);
                return res;
            }

            public FileDescriptorSet GetFileDescriptorSet(FileDescriptorProto proto)
            {
                return proto.Parent;
            }

            (string package, string name) GetPackageNameAndNamespace(IType proto)
            {
                string package = "";
                string name = "";
                IType cur = proto;
                IType topType = null;
                while (cur != null)
                {
                    if (cur.Parent is FileDescriptorProto)
                    {
                        topType = cur;
                        break;
                    }
                    cur = cur.Parent;
                }
                if (topType != null)
                {
                    FileDescriptorProto fdp = (FileDescriptorProto)topType.Parent;
                    package = fdp.Package;
                    if (FileInfo != fdp)
                    {
                        if (GetCodeNamespace != null)
                            name = GetCodeNamespace(fdp);
                        else
                        {
                            name = fdp.Options.CsharpNamespace;
                            if (string.IsNullOrEmpty(name))
                                name = fdp.Parent.DefaultPackage;
                        }
                    }
                }
                if (!package.StartsWith("."))
                    package = "." + package;
                return (package, name);
            }

            string GetFullQualifiedName(IType proto)
            {
                string name = "";
                IType cur = proto;
                while(cur !=null)
                {
                    if (cur is DescriptorProto pProto)
                    {
                        if (!string.IsNullOrEmpty(name))
                            name = pProto.Name + "." + name;
                        else
                            name = pProto.Name;
                    }
                    else if (cur is EnumDescriptorProto eProto)
                    {
                        if (!string.IsNullOrEmpty(name))
                            name = eProto.Name + "." + name;
                        else
                            name = eProto.Name;
                    }
                    else if (cur is FileDescriptorProto fProto)
                    {
                        if (!string.IsNullOrEmpty(name))
                            name = fProto.Package + "." + name;
                        else
                            name = fProto.Package;                        
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                    cur = cur.Parent;
                }
                if (!name.StartsWith("."))
                    name = "." + name;
                return name;
            }
        }
        public class EnumModel : ModelBase
        {
            public EnumDescriptorProto EnumInfo { get; set; }
            public int CurrentIdent { get; set; }
        }

        RazorLightEngine engine; 
        string templatePath;
        string typeTemplate;
        string enumTemplate;
        string fileTemplate;
        string globalTemplate;
        public RazorCodeGenerator()
        {
            engine = new RazorLightEngineBuilder()
            // required to have a default RazorLightProject type,
            // but not required to create a template from string.
            .UseEmbeddedResourcesProject(typeof(SingleFileModel))
            .SetOperatingAssembly(typeof(SingleFileModel).Assembly)
            .UseMemoryCachingProvider()
            .Build();

        }
        public override string Name => nameof(RazorCodeGenerator);

        public override IEnumerable<CodeFile> Generate(FileDescriptorSet set, NameNormalizer normalizer = null, Dictionary<string, string> options = null)
        {
            string extension = null;
            options?.TryGetValue("template_path", out templatePath);
            options?.TryGetValue("file_extension", out extension);
            if(extension == null)
            {
                extension = "";
            }
            if (!string.IsNullOrEmpty(templatePath) && Directory.Exists(templatePath))
            {
                string filePath = $"{templatePath}/type.tmpl";
                if(File.Exists(filePath))
                {
                    typeTemplate = File.ReadAllText(filePath);
                }
                filePath = $"{templatePath}/enum.tmpl";
                if (File.Exists(filePath))
                {
                    enumTemplate = File.ReadAllText(filePath);
                }
                filePath = $"{templatePath}/file.tmpl";
                if (File.Exists(filePath))
                {
                    fileTemplate = File.ReadAllText(filePath);
                }
                filePath = $"{templatePath}/global.tmpl";
                if (File.Exists(filePath))
                {
                    globalTemplate = File.ReadAllText(filePath);
                }
            }
            List<CodeFile> codes = new List<CodeFile>();
            bool hasTemplate = false;
            if(!string.IsNullOrEmpty(fileTemplate) && !string.IsNullOrEmpty(typeTemplate) && !string.IsNullOrEmpty(enumTemplate))
            {
                hasTemplate= true;
                foreach(var i in set.Files)
                {
                    if (string.IsNullOrEmpty(i.Options.CsharpNamespace))
                        i.Options.CsharpNamespace = set.DefaultPackage;
                    string path = Path.GetDirectoryName(i.Name);
                    string fn = Path.GetFileNameWithoutExtension(i.Name);
                    codes.Add(new CodeFile(Path.Combine(path, fn + extension), GenerateSingleFileCode(i)));
                }
            }
            if (!string.IsNullOrEmpty(globalTemplate))
            {
                hasTemplate = true;
            }

            if (!hasTemplate)
            {
                throw new ArgumentException("Please specify razor template path by using 'template_path' parameter");
            }
            return codes;
        }

        void InitializeModel(ModelBase model)
        {
            model.GenerateType = GenerateTypeCode;
            model.GenerateEnum = GenerateEnumCode;
        }

        FileDescriptorProto FindFileProto(DescriptorProto proto)
        {
            IType cur = proto;
            do
            {
                cur = cur.Parent;
                if (cur is FileDescriptorProto fdp)
                    return fdp;
            } while (cur != null);
            return null;
        }

        TypeModel MakeTypeModel(DescriptorProto proto, int ident)
        {
            TypeModel typeModel = new TypeModel();
            InitializeModel(typeModel);
            typeModel.CurrentIdent = ident;
            typeModel.TypeInfo = proto;
            typeModel.FileInfo = FindFileProto(proto);

            return typeModel;
        }

        EnumModel MakeEnumModel(EnumDescriptorProto proto, int ident)
        {
            EnumModel enumModel = new EnumModel();
            InitializeModel(enumModel);
            enumModel.CurrentIdent = ident;
            enumModel.EnumInfo = proto;
            return enumModel;
        }

        SingleFileModel MakeSingleFileModel(FileDescriptorProto proto)
        {
            SingleFileModel model = new SingleFileModel();
            InitializeModel(model);
            model.File = proto;
            return model;
        }

        string GenerateCode(string templateName, string template, ModelBase model, int ident)
        {
            var task = engine.CompileRenderStringAsync(templateName, template, model);
            task.Wait();
            var res = task.Result;
            if (ident > 0)
            {
                StringReader sr = new StringReader(res);
                StringBuilder final = new StringBuilder();
                while (sr.Peek() > 0)
                {
                    for (int i = 0; i < ident; i++)
                    {
                        final.Append("    ");
                    }
                    final.AppendLine(sr.ReadLine());
                }
                return final.ToString();
            }
            else
                return res;
        }

        string GenerateSingleFileCode(FileDescriptorProto proto)
        {
            return GenerateCode("FileTemplate", fileTemplate!, MakeSingleFileModel(proto), 0);
        }

        string GenerateTypeCode(DescriptorProto proto, int ident = 0)
        {
            return GenerateCode("TypeTemplate", typeTemplate!, MakeTypeModel(proto, ident), ident);
        }

        string GenerateEnumCode(EnumDescriptorProto proto, int ident = 0)
        {
            return GenerateCode("EnumTemplate", enumTemplate!, MakeEnumModel(proto, ident), ident);
        }

        
    }
}

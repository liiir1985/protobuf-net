using Google.Protobuf.Reflection;
using ProtoBuf;
using ProtoBuf.Reflection;
using RazorLight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace protogen.CodeGenerators
{
    internal class RazorCodeGenerator : CodeGenerator
    {
        delegate string TypeGenerationDelegate(DescriptorProto proto);
        delegate string EnumGenerationDelegate(EnumDescriptorProto proto);
        class ModelBase
        {
            public TypeGenerationDelegate GenerateType { get; set; }
            public EnumGenerationDelegate GenerateEnum { get; set; }
        }
        class SingleFileModel : ModelBase
        {
            public FileDescriptorProto File { get; set; }
        }
        class GlobalFileModel : ModelBase 
        {
            public FileDescriptorSet Files { get; set; }
        }
        class TypeModel : ModelBase
        {
            public DescriptorProto TypeInfo { get; set; }
        }
        class EnumModel : ModelBase
        {
            public EnumDescriptorProto EnumInfo { get; set; }
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
            
            options?.TryGetValue("template_path", out templatePath);
            if (string.IsNullOrEmpty(templatePath) || !Directory.Exists(templatePath))
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
                    codes.Add(new CodeFile(i.Name, GenerateSingleFileCode(i)));
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

        TypeModel MakeTypeModel(DescriptorProto proto)
        {
            TypeModel typeModel = new TypeModel();
            InitializeModel(typeModel);
            typeModel.TypeInfo = proto;

            return typeModel;
        }

        EnumModel MakeEnumModel(EnumDescriptorProto proto)
        {
            EnumModel enumModel = new EnumModel();
            InitializeModel(enumModel);
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

        string GenerateCode(string templateName, string template, ModelBase model)
        {
            var task = engine.CompileRenderStringAsync(templateName, template, model);
            task.Wait();
            return task.Result;
        }

        string GenerateSingleFileCode(FileDescriptorProto proto)
        {
            return GenerateCode("FileTemplate", fileTemplate!, MakeSingleFileModel(proto));
        }

        string GenerateTypeCode(DescriptorProto proto)
        {
            return GenerateCode("TypeTemplate", typeTemplate!, MakeTypeModel(proto));
        }

        string GenerateEnumCode(EnumDescriptorProto proto)
        {
            return GenerateCode("EnumTemplate", enumTemplate!, MakeEnumModel(proto));
        }
    }
}

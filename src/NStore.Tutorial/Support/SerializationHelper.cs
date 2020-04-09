using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NStore.Tutorial.Support
{
    public static class SerializationHelper
    {
        private class TutorialContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => base.CreateProperty(f, memberSerialization))
                    .Select(p =>
                    {
                        p.Readable = true;
                        p.Writable = true;
                        return p;
                    });

                var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                               BindingFlags.SetProperty)
                        .Select(p => base.CreateProperty(p, memberSerialization))
                        .Select(p =>
                        {
                            p.Readable = true;
                            return p;
                        })
                    ;

                return props.Union(fields).ToList();
            }
        }

        private static readonly JsonSerializerSettings Settings;

        static SerializationHelper()
        {
            Settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.All,
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                ContractResolver = new TutorialContractResolver()
            };
        }

        public static T DeepClone<T>(T source)
        {
            // * * * * * * * * * * * * * * * * * * * * * * *
            //                 DISCLAIMER                 //
            // * * * * * * * * * * * * * * * * * * * * * * *
            //
            // Just a sample, quick & dirty for the tutorial.
            // Don't do it for real
            //
            // * * * * * * * * * * * * * * * * * * * * * * *
            return JsonConvert.DeserializeObject<T>(
                JsonConvert.SerializeObject(source, Settings),
                Settings
            );
        }

        public static string ToJson(object source)
        {
            return JsonConvert.SerializeObject(source, Formatting.Indented);
        }
    }
}
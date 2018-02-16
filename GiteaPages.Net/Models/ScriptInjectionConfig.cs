using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GiteaPages.Net.Models {
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ScriptInjectionPosition {
        /// <summary>
        /// 於Header起始後載入
        /// </summary>
        Header_Start,
        /// <summary>
        /// 於Header結束前載入
        /// </summary>
        Header_End,
        /// <summary>
        /// 於Body結束前載入
        /// </summary>
        Body_End
    }

    public class ScriptInjectionConfig {
        public string Pattern { get; set; } = "*";
        public ScriptInjectionPosition Position { get; set; }
        public string Src { get; set; }
    }
}

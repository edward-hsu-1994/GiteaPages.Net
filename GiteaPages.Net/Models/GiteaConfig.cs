using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GiteaPages.Net.Models {
    public class GiteaConfig {
        public string Root { get; set; } = "";
        public string NotFound { get; set; } = "404.html";
        public ScriptInjectionConfig[] ScriptInjection { get; set; }
    }
}

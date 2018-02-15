using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GiteaPages.Net.Models {
    public class RepoInfo {
        public int Id { get; set; }
        public string User { get; set; }
        public string Repo { get; set; }
        public string LastCommitId { get; set; }
    }
}

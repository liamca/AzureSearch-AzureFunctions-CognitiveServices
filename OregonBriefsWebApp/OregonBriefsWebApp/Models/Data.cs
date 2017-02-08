using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace OregonBriefsWebApp.Models
{
    public class AutoCompleteItem
    {
        public string id { get; set; }
        public string desc { get; set; }
    }

    public class SearchResponse
    {
        public int TotalRows { get; set; }
        public List<Content> ContentList { get; set; }
    }

    public class Content
    {
        public string filename { get; set; }
        public string content { get; set; }
        public string summary { get; set; }
        public string phrases { get; set; }
    }

}
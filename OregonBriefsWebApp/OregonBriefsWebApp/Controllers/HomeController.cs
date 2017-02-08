using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using OregonBriefsWebApp.Models;

namespace OregonBriefsWebApp.Controllers
{
    public class HomeController : Controller
    {
        private static string indexName = "oregonbriefstr";

        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public ActionResult SearchAutoComplete(string term)
        {
            // Execute a autocomplete request which needs to leverage the REST api as it is still a private preview feature
            var searchAC = new Search();
            var data = searchAC.AutoCompleteSearch(term, indexName);
            List<string> suggestions = new List<string>();
            foreach (var result in data)
            {
                suggestions.Add(result.desc);
            }

            return new JsonResult
            {
                JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                Data = suggestions
            };
            //return Json(data);
        }

        public ActionResult SearchQuery(string search)
        {
            // Execute a autocomplete request which needs to leverage the REST api as it is still a private preview feature
            var searchContent = new Search();
            var data = searchContent.ExecuteSearch(search, indexName);
            return Json(data);
        }

    }

}
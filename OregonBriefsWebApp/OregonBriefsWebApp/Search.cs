//Copyright 2014 Microsoft
//Licensed under the MIT License

//Based on sample code provided at https://github.com/twitter/typeahead.js
//Data and Images provided by Wikipedia - http://en.wikipedia.org/wiki/List_of_vegetables 

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using OregonBriefsWebApp;
using OregonBriefsWebApp.Models;
using Newtonsoft.Json.Linq;

namespace OregonBriefsWebApp
{
    public class Search
    {
        private static readonly Uri _serviceUri;
        private static HttpClient _httpClient;
        public static string errorMessage;

        static Search()
        {
            // We will use REST since I am using autocomplete which is currently private preview and not yet in .NET SDK
            _serviceUri = new Uri("https://" + ConfigurationManager.AppSettings["SearchServiceName"] + ".search.windows.net");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("api-key", ConfigurationManager.AppSettings["SearchServiceApiKey"]);
        }


        public dynamic AutoCompleteSearch(string searchText, string indexName)
        {
            // Pass the specified suggestion text and return the fields
            Uri uri = new Uri(_serviceUri, "/indexes/" + indexName + "/docs/autocomplete?suggesterName=sg&$top=7&autoCompleteMode=oneterm&search=" + Uri.EscapeDataString(searchText));
            HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Get, uri);
            AzureSearchHelper.EnsureSuccessfulSearchResponse(response);
            List<AutoCompleteItem> aciList = new List<AutoCompleteItem>();

            foreach (var option in AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result).value)
            {
                AutoCompleteItem aci = new AutoCompleteItem();
                aci.id = (string)option["id"];
                aci.desc = (string)option["queryPlusText"];
                aciList.Add(aci);
            }

            return aciList;
        }

        public dynamic ExecuteSearch(string searchText, string indexName)
        {
            // Pass the specified suggestion text and return the fields
            //Uri uri = new Uri(_serviceUri, "/indexes/" + indexName + "/docs?$top=10&queryType=full&highlight=content&highlightPreTag=<b>&highlightPostTag=</b>&searchFields=content&$select=metadata_storage_path,content&$count=true&search=" + Uri.EscapeDataString(searchText));
            //Uri uri = new Uri(_serviceUri, "/indexes/" + indexName + "/docs?$top=10&queryType=full&highlight=content&highlightPreTag=<b>&highlightPostTag=</b>&searchFields=content,ocr&$select=metadata_storage_path,content,ocr&$count=true&search=" + Uri.EscapeDataString(searchText));
            Uri uri = new Uri(_serviceUri, "/indexes/" + indexName + "/docs?$top=10&queryType=full&highlight=content&highlightPreTag=<b>&highlightPostTag=</b>&searchFields=content,ocr&$select=metadata_storage_path,content,ocr,summary,keyPhrases&$count=true&search=" + Uri.EscapeDataString(searchText));
            HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Get, uri);
            AzureSearchHelper.EnsureSuccessfulSearchResponse(response);

            SearchResponse sr = new SearchResponse();
            sr.ContentList = new List<Content>();

            sr.TotalRows = Convert.ToInt32(AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result)["@odata.count"]);
            // Load the content
            foreach (var option in AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result).value)
            {
                Content c = new Content();
                c.filename = (string)option["metadata_storage_path"];
                //c.summary = ((string)option["content"]);
                c.summary = ((string)option["summary"]);
                c.phrases = ((string)option["keyPhrases"]);
                if (option["@search.highlights"] != null)
                {
                    foreach (var highlight in option["@search.highlights"].content)
                    {
                        c.content += ">>> " + highlight + "<br>";
                    }
                }
                sr.ContentList.Add(c);
            }
            

            return sr;
        }


    }
}



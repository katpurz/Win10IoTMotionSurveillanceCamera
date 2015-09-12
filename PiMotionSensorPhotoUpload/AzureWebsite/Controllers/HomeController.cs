using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Web.Configuration;

namespace AzureWebsite.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            //check for pictures - then add them to webpage
            string connectionString = WebConfigurationManager.AppSettings["StorageConnectionString"];
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference(WebConfigurationManager.AppSettings["AzureStorageAccountContainer"]);

            //a list of all the pictures in container 
            //we'll use this to find the latest 6 pictures
            List<blobProperties> listBlobProperties = new List<blobProperties>();

            // Loop over container's items
            foreach (IListBlobItem item in container.ListBlobs(null, false))
            {
                if (item.GetType() == typeof(CloudBlockBlob))
                {
                    CloudBlockBlob blob = (CloudBlockBlob)item;
                    listBlobProperties.Add(new blobProperties(blob.Properties.LastModified, blob.Uri));
                }
            }

            var last6 = (from x in listBlobProperties
                         orderby x.dateModified descending
                         select x).Take(6);

            //build the HTML to show the pictures
            ViewBag.PicArea += "<table><tr>";
            foreach (blobProperties blob in last6)
            {
                ViewBag.PicArea += "<td><img src=\"" + blob.Uri + "\" alt=\"" + blob.Uri + "\" style=\"width:304px;height:228px;\" />"+ blob.dateModified.ToString()+"</td>";
            }
            ViewBag.PicArea += "</tr></table>";

            return View();
        }


        public class blobProperties
        {
            public DateTimeOffset? dateModified;
            public Uri Uri;
            public blobProperties(DateTimeOffset? _dateModified, Uri _Uri)
            {
                dateModified = _dateModified; Uri = _Uri;
            }
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }
    }
}
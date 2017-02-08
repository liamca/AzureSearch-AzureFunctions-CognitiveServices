using System;
using System.Configuration;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace TextAnalysis
{
    class Program
    {
        static string BlobConnectionString = ConfigurationManager.AppSettings["BlobStorageConnectionString"];
        const string ContainerName = "oregonbriefs";

        static void Main(string[] args)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(BlobConnectionString);
            
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(ContainerName);

            TraceWriter tw = new TC();
            foreach (IListBlobItem item in container.ListBlobs(null, true))
            {
                // Retrieve reference to a blob named "photo1.jpg".
                CloudBlockBlob blockBlob = (CloudBlockBlob)item;

                // Save blob contents to a file.
                using (var stream = new MemoryStream())
                {
                    blockBlob.DownloadToStream(stream);
                    stream.Seek(0, SeekOrigin.Begin);
                    OCR.Run(stream, blockBlob.Name, tw);
                }
            }
        }
    }

    class TC : TraceWriter
    {
        public TC() : base(System.Diagnostics.TraceLevel.Verbose) { }

        public override void Trace(TraceEvent traceEvent)
        {
            Console.WriteLine(traceEvent.Message);
        }
    }
}

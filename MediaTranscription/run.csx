using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

// Media services credentials
private static readonly string _mediaServicesAccountName = "XXX";
private static readonly string _mediaServicesAccountKey = "XXX";

// Normally you would pass this file in a Queue or as part of the HTTP request
private static string url = "https://XXX.blob.core.windows.net/media/Blah.mp3";

// Field for service context.
private static CloudMediaContext _context = null;
private static MediaServicesCredentials _cachedCredentials = null;

public static void Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

    // Create and cache the Media Services credentials in a static class variable.
    _cachedCredentials = new MediaServicesCredentials(_mediaServicesAccountName,_mediaServicesAccountKey);
    // Used the cached credentials to create CloudMediaContext.
    _context = new CloudMediaContext(_cachedCredentials);

    // Run indexing jobs.
    log.Info(url);
    var asset = RunIndexingJob(log);

    // Download the job output asset to a string.
    string text = DownloadAsset(asset);

    // Output the text
    log.Info(text);

    // Delete asset as I do not need it
    asset.Delete();
}

static IAsset RunIndexingJob(TraceWriter log)
{
    // Create an asset and upload the input media file to storage.
    IAsset asset = CreateAssetAndUploadSingleBlob(url,
        "My Indexing Input Asset",
        AssetCreationOptions.None);

    // Declare a new job.
    IJob job = _context.Jobs.Create("My Indexing Job");

    // Get a reference to Azure Media Indexer 2 Preview.
    string MediaProcessorName = "Azure Media Indexer 2 Preview";

    var processor = GetLatestMediaProcessorByName(MediaProcessorName);

    // Read configuration from the specified file.
    string configuration = "{ " +
        "   \"version\":\"1.0\", " +
        "   \"Features\": " +
        "     [" +
        "        { " +
        "        \"Options\": { " +
        "             \"Formats\":[\"WebVtt\"], " +
        "             \"Language\":\"enUs\", " +
        "             \"Type\":\"RecoOptions\" " +
        "        }, " +
        "        \"Type\":\"SpReco\" " +
        "     }] " +
        " } ";

    // Create a task with the encoding details, using a string preset.
    ITask task = job.Tasks.AddNew("My Indexing Task",
        processor,
        configuration,
        TaskOptions.None);

    // Specify the input asset to be indexed.
    task.InputAssets.Add(asset);

    // Add an output asset to contain the results of the job.
    task.OutputAssets.AddNew("My Indexing Output Asset", AssetCreationOptions.None);

    // Use the following event handler to check job progress.  
    job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);

    // Launch the job.
    job.Submit();

    // Check job execution and wait for job to finish.
    Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);

    progressJobTask.Wait();

    // If job state is Error, the event handling
    // method for job progress should log errors.  Here we check
    // for error state and exit if needed.
    if (job.State == JobState.Error)
    {
        ErrorDetail error = job.Tasks.First().ErrorDetails.First();
        log.Info(string.Format("Error: {0}. {1}",
                                        error.Code,
                                        error.Message));
        return null;
    }

    return job.OutputMediaAssets[0];
}

//static IAsset CreateAssetAndUploadSingleFile(string filePath, string assetName, AssetCreationOptions options)
//{
//    IAsset asset = _context.Assets.Create(assetName, options);

//    var assetFile = asset.AssetFiles.Create(Path.GetFileName(filePath));
//    assetFile.Upload(filePath);

//    return asset;
//}

static IAsset CreateAssetAndUploadSingleBlob(string fileURL, string assetName, AssetCreationOptions options)
{
    IAsset asset = _context.Assets.Create(assetName, options);

    var assetFile = asset.AssetFiles.Create(fileURL.Substring(fileURL.LastIndexOf("/")+1));

    WebClient wc = new WebClient();
    using (MemoryStream stream = new MemoryStream(wc.DownloadData(fileURL)))
    {
        assetFile.Upload(stream);
    }

    

    return asset;
}


static string DownloadAsset(IAsset asset)
{
    //file.Download(Path.Combine(outputDirectory, file.Name));

    string text = string.Empty;
    string blobConectionString = "DefaultEndpointsProtocol=https;AccountName=azsearchliamca;AccountKey=FqsDdjnXm8H4nq7PUL0CtJp/E1VIdiJEmU9e7/wA646Cc9oMtTj4vA67L7PAdn3UQ9k0n8qJsFm1zaWDJk7rIw==";

    foreach (IAssetFile file in asset.AssetFiles)
    {
        string containerName = file.Asset.Uri.ToString().Substring(file.Asset.Uri.ToString().LastIndexOf("/") + 1);

        // Retrieve storage account from connection string.
        CloudStorageAccount storageAccount = CloudStorageAccount.Parse(blobConectionString);

        // Create the blob client.
        CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

        // Retrieve reference to a previously created container.
        CloudBlobContainer container = blobClient.GetContainerReference(containerName);

        // Retrieve reference to a blob named "filename"
        CloudBlockBlob blockBlob2 = container.GetBlockBlobReference(file.Name);

        using (var memoryStream = new MemoryStream())
        {
            blockBlob2.DownloadToStream(memoryStream);
            text = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
        }

        // Delete this container as I no longer need it
        container.Delete();
    }

    return text;

}

static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
{
    var processor = _context.MediaProcessors
        .Where(p => p.Name == mediaProcessorName)
        .ToList()
        .OrderBy(p => new Version(p.Version))
        .LastOrDefault();

    if (processor == null)
        throw new ArgumentException(string.Format("Unknown media processor",
                                                    mediaProcessorName));

    return processor;
}

static private void StateChanged(object sender, JobStateChangedEventArgs e)
{
    switch (e.CurrentState)
    {
        case JobState.Finished:
            break;
        case JobState.Canceling:
        case JobState.Queued:
        case JobState.Scheduled:
        case JobState.Processing:
            break;
        case JobState.Canceled:
        case JobState.Error:
            // Cast sender as a job.
            IJob job = (IJob)sender;
            // Display or log error details as needed.
            // LogJobStop(job.Id);
            break;
        default:
            break;
    }
}

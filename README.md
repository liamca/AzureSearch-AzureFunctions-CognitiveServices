# How To Enrich Unstructured Documents in an Azure Search Index
Set of useful Azure Functions for doing advanced text processing such as Text Summarization and OCR on unstructured documents using Cognitive Services and Azure Functions

The goal of these Azure Functions is to allow you to do additional processing over unstructured contents stored in Azure Blob's such as PDF and Office Docs.  The results of the processing allows you to enrich your Azure Search index with additional content.  For example, if there are images in your PDF, you can leverage Azure Cognitive Services to extract the text from the images which are then added to the Azure Search index to be searchable.  Below are the examples currently available.

##Requirements
- [Azure Search Service](https://azure.microsoft.com/en-us/services/search/) 
- [Azure Blob Storage](https://docs.microsoft.com/en-us/azure/storage/) with PDF's stored
- [Azure Functions](https://docs.microsoft.com/en-us/azure/azure-functions/)
- [Azure Cognitive Services](https://docs.microsoft.com/en-us/azure/cognitive-services/) ([Text Analytics API](https://www.microsoft.com/cognitive-services/en-us/text-analytics/documentation) for Text Summarization & [Vision API](https://www.microsoft.com/cognitive-services/en-us/computer-vision-api/documentation) for OCR)

##OCR - Optical Character Recognition
This includes an Azure Functions that leverages the [Azure Blob Trigger](https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob) that calls an Azure Function whenever a file is added to the configured blob store.  This function extracts the images from the PDF and then calls the Azure Cognitive Services Vision API to extract the text from them images which is then added to your Azure Serach index.  Currently it only supports PDF processing, but could easily be extended for other file types.

##Text Summarization
This includes an Azure Functions that leverages the [Azure Blob Trigger](https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob) that calls an Azure Function whenever a file is added to the configured blob store.  This function extracts the text from the PDF and then calls the Azure Cognitive Services Text Analytics API to extract the key phrases from the text.  The top phrases are then used to find sentences in the text which best represent the overall text.  This summarized test is then added to your Azure Serach index.  Currently it only supports PDF processing, but could easily be extended for other file types.

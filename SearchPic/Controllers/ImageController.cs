using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Azure.Storage.Blobs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using SearchPic.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Sas;

namespace SearchPic.Controllers
{
    public class ImageController : Controller
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly Container _cosmosContainer;
        public ImageController(BlobServiceClient blobServiceClient, Container cosmosContainer)
        {
            _blobServiceClient = blobServiceClient;
            _cosmosContainer = cosmosContainer;
        }
        private async Task<(string Description, List<string> Keywords)> AnalyzeImage(string imageUrl)
        {
            var subscriptionKey = "";
            var endpoint = "";
            var client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(subscriptionKey))
            {
                Endpoint = endpoint
            };
            var features = new List<VisualFeatureTypes?> { VisualFeatureTypes.Description, VisualFeatureTypes.Tags };
            try
            {
                ImageAnalysis analysis = await client.AnalyzeImageAsync(imageUrl, features);
                string description = analysis.Description.Captions.FirstOrDefault()?.Text ?? "No description available";
                var keywords = analysis.Tags.Select(tag => tag.Name).ToList();
                return (description, keywords);
            }
            catch (ComputerVisionErrorResponseException ex)
            {
                Console.WriteLine($"Error analyzing image: {ex.Message}");
                throw;
            }
        }
        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }
            var blobContainer = _blobServiceClient.GetBlobContainerClient("images");
            var blobClient = blobContainer.GetBlobClient(file.FileName);
            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream);
            }
            var sasToken = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));
            var imageUrlWithSas = sasToken.ToString();
            var (description, keywords) = await AnalyzeImage(imageUrlWithSas);
            var imageMetadata = new LocalImageMetadata
            {
                Id = Guid.NewGuid().ToString(),
                Description = description,
                Keywords = keywords,
                ImageUrl = imageUrlWithSas
            };
            try
            {
                await _cosmosContainer.CreateItemAsync(imageMetadata);
            }
            catch (CosmosException ex)
            {
                Console.WriteLine($"CosmosDB error: {ex.Message}");
                return BadRequest("Failed to save image metadata.");
            }
            return View("UploadConfirmation", imageMetadata);
        }
        [HttpGet]
        public IActionResult Search()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Search(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return BadRequest("Keyword cannot be empty.");
            }
            var query = new QueryDefinition("SELECT * FROM c WHERE ARRAY_CONTAINS(c.keywords, @keyword)")
                .WithParameter("@keyword", keyword);
            var iterator = _cosmosContainer.GetItemQueryIterator<LocalImageMetadata>(query);
            var results = new List<LocalImageMetadata>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }
            return View(results);
        }
    }
}

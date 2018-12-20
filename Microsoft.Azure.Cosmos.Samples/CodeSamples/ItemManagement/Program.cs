﻿using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Cosmos.Samples.Shared
{
    // ----------------------------------------------------------------------------------------------------------
    // Prerequisites - 
    // 
    // 1. An Azure Cosmos account - 
    //    https://azure.microsoft.com/en-us/itemation/articles/itemdb-create-account/
    //
    // 2. Microsoft.Azure.Cosmos NuGet package - 
    //    http://www.nuget.org/packages/Microsoft.Azure.Cosmos/ 
    // ----------------------------------------------------------------------------------------------------------
    // Sample - demonstrates the basic CRUD operations on a Item resource for Azure Cosmos
    //
    // 1. Basic CRUD operations on a item using regular POCOs
    // 1.1 - Create a item
    // 1.2 - Read a item by its Id
    // 1.3 - Read all items in a Collection
    // 1.4 - Query for items by a property other than Id
    // 1.5 - Replace a item
    // 1.6 - Upsert a item
    // 1.7 - Delete a item
    //
    // 2. Work with dynamic objects
    //
    // 3. Using ETags to control execution
    // 3.1 - Use ETag with ReplaceItem for optimistic concurrency
    // 3.2 - Use ETag with ReadItem to only return a result if the ETag of the request does not match
    //-----------------------------------------------------------------------------------------------------------
    // See Also - 
    //
    // Cosmos.Samples.Queries -           We only included a VERY basic query here for completeness,
    //                                        For a detailed exploration of how to query for Items, 
    //                                        including how to paginate results of queries.
    //
    // Cosmos.Samples.ServerSideScripts - In these examples we do simple loops to create small numbers
    //                                        of items. For insert operations where you are creating many
    //                                        items we recommend using a Stored Procedure and pass batches
    //                                        of new items to this sproc. Consult this sample for an example
    //                                        of a BulkInsert stored procedure. 
    // ----------------------------------------------------------------------------------------------------------

    public class Program
    {
        private static readonly string databaseId = "samples";
        private static readonly string containerId = "item-samples";

        //Reusable instance of ItemClient which represents the connection to a Cosmos endpoint
        private static CosmosDatabase database = null;
        private static CosmosContainer container = null;
        public static void Main(string[] args)
        {
            try
            {
                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .AddJsonFile("appSettings.json")
                    .Build();

                string endpoint = configuration["EndPointUrl"];
                if (string.IsNullOrEmpty(endpoint))
                {
                    throw new ArgumentNullException("Please specify a valid endpoint in the appSettings.json");
                }

                string authKey = configuration["AuthorizationKey"];
                if (string.IsNullOrEmpty(authKey) || string.Equals(authKey, "Super secret key"))
                {
                    throw new ArgumentException("Please specify a valid AuthorizationKey in the appSettings.json");
                }

                //Read the Cosmos endpointUrl and authorisationKeys from configuration
                //These values are available from the Azure Management Portal on the Cosmos Account Blade under "Keys"
                //NB > Keep these values in a safe & secure location. Together they provide Administrative access to your Cosmos account
                using (CosmosClient client = new CosmosClient(endpoint, authKey))
                {
                    Program.Initialize(client).GetAwaiter().GetResult();
                    Program.RunItemsDemo().GetAwaiter().GetResult();
                    Program.Cleanup().GetAwaiter().GetResult();
                }
            }
            catch (CosmosException cre)
            {
                Console.WriteLine(cre.ToString());
            }
            catch (Exception e)
            {
                Exception baseException = e.GetBaseException();
                Console.WriteLine("Error: {0}, Message: {1}", e.Message, baseException.Message);
            }
            finally
            {
                Console.WriteLine("End of demo, press any key to exit.");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Run basic item access methods as a console app demo
        /// </summary>
        /// <returns></returns>
        private static async Task RunItemsDemo()
        {
            await Program.RunBasicOperationsOnStronglyTypedObjects();

            await Program.RunBasicOperationsOnDynamicObjects();

            await Program.UseETags();

            await Program.UseConsistencyLevels();
        }

        /// <summary>
        /// 1. Basic CRUD operations on a item
        /// 1.1 - Create a item
        /// 1.2 - Read a item by its Id
        /// 1.3 - Read all items in a Collection
        /// 1.4 - Query for items by a property other than Id
        /// 1.5 - Replace a item
        /// 1.6 - Upsert a item
        /// 1.7 - Delete a item
        /// </summary>
        private static async Task RunBasicOperationsOnStronglyTypedObjects()
        {
            await Program.CreateItemsAsync();

            await Program.ReadItemAsync();

            SalesOrder result = await Program.QueryItems();

            await Program.ReplaceItemAsync(result);

            await Program.UpsertItemAsync();

            await Program.DeleteItemAsync();
        }

        private static async Task CreateItemsAsync()
        {
            Console.WriteLine("\n1.1 - Creating items");

            // Create a SalesOrder object. This object has nested properties and various types including numbers, DateTimes and strings.
            // This can be saved as JSON as is without converting into rows/columns.
            SalesOrder salesOrder = GetSalesOrderSample("SalesOrder1");
            await container.Items.CreateItemAsync(salesOrder.AccountNumber, salesOrder);

            // As your app evolves, let's say your object has a new schema. You can insert SalesOrderV2 objects without any 
            // changes to the database tier.
            SalesOrder2 newSalesOrder = GetSalesOrderV2Sample("SalesOrder2");
            await container.Items.CreateItemAsync(newSalesOrder.AccountNumber, newSalesOrder);
        }

        private static async Task ReadItemAsync()
        {
            Console.WriteLine("\n1.2 - Reading Item by Id");

            // Note that Reads require a partition key to be specified. This can be skipped if your collection is not
            // partitioned i.e. does not have a partition key definition during creation.
            CosmosItemResponse<SalesOrder> response = await container.Items.ReadItemAsync<SalesOrder>(
                partitionKey: "Account1",
                id: "SalesOrder1");

            // You can measure the throughput consumed by any operation by inspecting the RequestCharge property
            Console.WriteLine("Item read by Id {0}", response.Resource);
            Console.WriteLine("Request Units Charge for reading a Item by Id {0}", response.RequestCharge);

            SalesOrder readOrder = (SalesOrder)response;
        }

        private static async Task<SalesOrder> QueryItems()
        {
            //******************************************************************************************************************
            // 1.4 - Query for items by a property other than Id
            //
            // NOTE: Operations like AsEnumerable(), ToList(), ToArray() will make as many trips to the database
            //       as required to fetch the entire result-set. Even if you set MaxItemCount to a smaller number. 
            //       MaxItemCount just controls how many results to fetch each trip. 
            //       If you don't want to fetch the full set of results, then use CreateItemQuery().AsItemQuery()
            //       For more on this please refer to the Queries project.
            //
            // NOTE: If you want to get the RU charge for a query you also need to use CreateItemQuery().AsItemQuery()
            //       and check the RequestCharge property of this IQueryable response
            //       Once again, refer to the Queries project for more information and examples of this
            //******************************************************************************************************************
            Console.WriteLine("\n1.4 - Querying for a item using its AccountNumber property");

            CosmosSqlQueryDefinition query = new CosmosSqlQueryDefinition(
                "select * from sales s where s.AccountNumber = @AccountInput ")
                .UseParameter("@AccountInput", "Account1");

            CosmosResultSetIterator<SalesOrder> resultSet = container.Items.CreateItemQuery<SalesOrder>(
                query, maxConcurrency: 1,
                maxItemCount: 1);

            while (resultSet.HasMoreResults)
            {
                SalesOrder sale = (await resultSet.FetchNextSetAsync()).First();
                Console.WriteLine($"Account Number: {sale.AccountNumber}; Id: {sale.Id} ");
                return sale;
            }

            throw new ArgumentNullException("No Sale order found by query");
        }

        private static async Task ReplaceItemAsync(SalesOrder order)
        {
            //******************************************************************************************************************
            // 1.5 - Replace a item
            //
            // Just update a property on an existing item and issue a Replace command
            //******************************************************************************************************************
            Console.WriteLine("\n1.5 - Replacing a item using its Id");

            order.ShippedDate = DateTime.UtcNow;
            CosmosItemResponse<SalesOrder> response = await container.Items.ReplaceItemAsync(
                partitionKey: order.AccountNumber,
                id: order.Id,
                item: order);

            SalesOrder updated = response.Resource;
            Console.WriteLine($"Request charge of replace operation: {response.RequestCharge}");
            Console.WriteLine($"Shipped date of updated item: {updated.ShippedDate}");
        }

        private static async Task UpsertItemAsync()
        {
            Console.WriteLine("\n1.6 - Upserting a item");

            SalesOrder upsertOrder = GetSalesOrderSample("SalesOrder3");
            CosmosItemResponse<SalesOrder> response = await container.Items.UpsertItemAsync(
                partitionKey: upsertOrder.AccountNumber, 
                item: upsertOrder);

            SalesOrder upserted = response.Resource;
            Console.WriteLine($"Request charge of upsert operation: {response.RequestCharge}");
            Console.WriteLine($"StatusCode of this operation: { response.StatusCode}");
            Console.WriteLine($"Id of upserted item: {upserted.Id}");
            Console.WriteLine($"AccountNumber of upserted item: {upserted.AccountNumber}");

            upserted.AccountNumber = "updated account number";
            response = await container.Items.UpsertItemAsync(partitionKey: upserted.AccountNumber, item: upserted);
            upserted = response.Resource;

            Console.WriteLine($"Request charge of upsert operation: {response.RequestCharge}");
            Console.WriteLine($"StatusCode of this operation: { response.StatusCode}");
            Console.WriteLine($"Id of upserted item: {upserted.Id}");
            Console.WriteLine($"AccountNumber of upserted item: {upserted.AccountNumber}");
        }

        private static async Task DeleteItemAsync()
        {
            Console.WriteLine("\n1.7 - Deleting a item");
            CosmosItemResponse<SalesOrder> response = await container.Items.DeleteItemAsync<SalesOrder>(
                partitionKey: "Account1",
                id: "SalesOrder3");

            Console.WriteLine("Request charge of delete operation: {0}", response.RequestCharge);
            Console.WriteLine("StatusCode of operation: {0}", response.StatusCode);
        }

        private static SalesOrder GetSalesOrderSample(string itemId)
        {
            SalesOrder salesOrder = new SalesOrder
            {
                Id = itemId,
                AccountNumber = "Account1",
                PurchaseOrderNumber = "PO18009186470",
                OrderDate = new DateTime(2005, 7, 1),
                SubTotal = 419.4589m,
                TaxAmount = 12.5838m,
                Freight = 472.3108m,
                TotalDue = 985.018m,
                Items = new SalesOrderDetail[]
                {
                    new SalesOrderDetail
                    {
                        OrderQty = 1,
                        ProductId = 760,
                        UnitPrice = 419.4589m,
                        LineTotal = 419.4589m
                    }
                },
            };

            // Set the "ttl" property to auto-expire sales orders in 30 days 
            salesOrder.TimeToLive = 60 * 60 * 24 * 30;

            return salesOrder;
        }

        private static SalesOrder2 GetSalesOrderV2Sample(string itemId)
        {
            return new SalesOrder2
            {
                Id = itemId,
                AccountNumber = "Account2",
                PurchaseOrderNumber = "PO15428132599",
                OrderDate = new DateTime(2005, 7, 1),
                DueDate = new DateTime(2005, 7, 13),
                ShippedDate = new DateTime(2005, 7, 8),
                SubTotal = 6107.0820m,
                TaxAmt = 586.1203m,
                Freight = 183.1626m,
                DiscountAmt = 1982.872m,            // new property added to SalesOrder2
                TotalDue = 4893.3929m,
                Items = new SalesOrderDetail2[]
                {
                    new SalesOrderDetail2
                    {
                        OrderQty = 3,
                        ProductCode = "A-123",      // notice how in SalesOrderDetail2 we no longer reference a ProductId
                        ProductName = "Product 1",  // instead we have decided to denormalize our schema and include 
                        CurrencySymbol = "$",       // the Product details relevant to the Order on to the Order directly
                        CurrencyCode = "USD",       // this is a typical refactor that happens in the course of an application
                        UnitPrice = 17.1m,          // that would have previously required schema changes and data migrations etc. 
                        LineTotal = 5.7m
                    }
                }
            };
        }

        /// <summary>
        /// 2. Basic CRUD operations using dynamics instead of strongly typed objects
        /// Cosmos does not require objects to be typed. Applications that merge data from different data sources, or 
        /// need to handle evolving schemas can write data directly as JSON or dynamic objects.
        /// </summary>
        private static async Task RunBasicOperationsOnDynamicObjects()
        {
            Console.WriteLine("\n2. Use Dynamics");

            // Create a dynamic object
            dynamic salesOrder = new
            {
                id = "_SalesOrder5",
                AccountNumber = "NewUser01",
                PurchaseOrderNumber = "PO18009186470",
                OrderDate = DateTime.UtcNow,
                Total = 5.95,
            };
            
            Console.WriteLine("\nCreating item");

            CosmosItemResponse<dynamic> response = await container.Items.CreateItemAsync<dynamic>("NewUser01", salesOrder);
            dynamic createdItem = response.Resource;

            Console.WriteLine("Item with id {0} created", createdItem.Id);
            Console.WriteLine("Request charge of operation: {0}", response.RequestCharge);

            response = await container.Items.ReadItemAsync<dynamic>(partitionKey: "NewUser01", id: "_SalesOrder5");

            dynamic readItem = response.Resource;

            //update a dynamic object by just creating a new Property on the fly
            //Item is itself a dynamic object, so you can just use this directly too if you prefer
            readItem.Add("shippedDate", DateTime.UtcNow);

            //if you wish to work with a dynamic object so you don't need to use SetPropertyValue() or GetPropertyValue<T>()
            //then you can cast to a dynamic
            salesOrder = readItem;
            salesOrder.foo = "bar";

            //now do a replace using this dynamic item
            //everything that is needed is contained in the readDynOrder object 
            //it has a .self Property
            Console.WriteLine("\nReplacing item");

            response = await container.Items.ReplaceItemAsync<dynamic>(partitionKey: "NewUser01", id: "_SalesOrder5", item: salesOrder);
            dynamic replaced = response.Resource;

            Console.WriteLine("Request charge of operation: {0}", response.RequestCharge);
            Console.WriteLine("shippedDate: {0} and foo: {1} of replaced item", replaced.shippedDate, replaced.foo);
        }

        /// <summary>
        /// 3. Using ETags to control execution of operations
        /// 3.1 - Use ETag to control if a ReplaceItem operation should check if ETag of request matches Item
        /// 3.2 - Use ETag to control if ReadItem should only return a result if the ETag of the request does not match the Item
        /// </summary>
        /// <returns></returns>
        private static async Task UseETags()
        {
            //******************************************************************************************************************
            // 3.1 - Use ETag to control if a replace should succeed, or not, based on whether the ETag on the request matches
            //       the current ETag value of the persisted Item
            //
            // All items in Cosmos have an _etag field. This gets set on the server every time a item is updated.
            // 
            // When doing a replace of a item you can opt-in to having the server only apply the Replace if the ETag 
            // on the request matches the ETag of the item on the server.
            // If someone did an update to the same item since you read it, then the ETag on the server will not match
            // and the Replace operation can be rejected. 
            //******************************************************************************************************************
            Console.WriteLine("\n3.1 - Using optimistic concurrency when doing a ReplaceItemAsync");

            //read a item
            CosmosItemResponse<SalesOrder> itemResponse = await container.Items.ReadItemAsync<SalesOrder>(
                partitionKey: "Account1", 
                id: "SalesOrder1");

            Console.WriteLine("ETag of read item - {0}", itemResponse.ETag);

            SalesOrder item = itemResponse;
            //Update the total due
            itemResponse.Resource.TotalDue = 1000000;

            //persist the change back to the server
            CosmosItemResponse<SalesOrder> updatedDoc = await container.Items.ReplaceItemAsync<SalesOrder>(
                partitionKey: item.AccountNumber, 
                id: item.Id, 
                item: item);

            Console.WriteLine("ETag of item now that is has been updated - {0}", updatedDoc.ETag);

            //now, using the originally retrieved item do another update 
            //but set the AccessCondition class with the ETag of the originally read item and also set the AccessConditionType
            //this tells the service to only do this operation if ETag on the request matches the current ETag on the item
            //in our case it won't, because we updated the item and therefore gave it a new ETag
            try
            {
                AccessCondition ac = new AccessCondition { Condition = itemResponse.ETag, Type = AccessConditionType.IfMatch };
                itemResponse.Resource.TotalDue = 9999999;
                updatedDoc = await container.Items.ReplaceItemAsync<SalesOrder>(item.AccountNumber, item.Id, itemResponse, new CosmosItemRequestOptions { AccessCondition = ac });
            }
            catch (CosmosException cre)
            {
                //   now notice the failure when attempting the update 
                //   this is because the ETag on the server no longer matches the ETag of doc (b/c it was changed in step 2)
                if (cre.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    Console.WriteLine("As expected, we have a pre-condition failure exception\n");
                }
            }

            //*******************************************************************************************************************
            // 3.2 - ETag on a ReadItemAsync request can be used to tell the server whether it should return a result, or not
            //
            // By setting the ETag on a ReadItemRequest along with an AccessCondition of IfNoneMatch instructs the server
            // to only return a result if the ETag of the request does not match that of the persisted Item
            //*******************************************************************************************************************

            Console.WriteLine("\n3.2 - Using ETag to do a conditional ReadItemAsync");

            // Get a item
            CosmosItemResponse<SalesOrder> response = await container.Items.ReadItemAsync<SalesOrder>(partitionKey: "Account2", id: "SalesOrder2");

            item = response;
            Console.WriteLine($"Read doc with StatusCode of {response.StatusCode}");

            // Get the item again with conditional access set, no item should be returned
            AccessCondition accessCondition = new AccessCondition
            {
                Condition = itemResponse.ETag,
                Type = AccessConditionType.IfNoneMatch
            };

            response = await container.Items.ReadItemAsync<SalesOrder>(
                partitionKey: "Account2", 
                id: "SalesOrder2", 
                requestOptions: new CosmosItemRequestOptions() { AccessCondition = accessCondition });

            Console.WriteLine("Read doc with StatusCode of {0}", response.StatusCode);

            // Now change something on the item, then do another get and this time we should get the item back
            response.Resource.TotalDue = 42;

            response = await container.Items.ReplaceItemAsync<SalesOrder>(item.AccountNumber, item.Id, item);

            response = await container.Items.ReadItemAsync<SalesOrder>(
                partitionKey: "Account2", 
                id: "SalesOrder2", 
                requestOptions: new CosmosItemRequestOptions() { AccessCondition = accessCondition });


            Console.WriteLine("Read doc with StatusCode of {0}", response.StatusCode);
        }

        private static async Task UseConsistencyLevels()
        {
            // Override the consistency level for a read request
            CosmosItemResponse<SalesOrder> response = await container.Items.ReadItemAsync<SalesOrder>(
                partitionKey: "Account2",
                id: "SalesOrder2",
                requestOptions: new CosmosItemRequestOptions() { ConsistencyLevel = ConsistencyLevel.Eventual });
        }

        private static async Task Cleanup()
        {
            if (database != null)
            {
                await database.DeleteAsync();
            }
        }

        private static async Task Initialize(CosmosClient client)
        {
            database = await client.Databases.CreateDatabaseIfNotExistsAsync(databaseId);

            // Delete the existing container to prevent create item conflicts
            await database.Containers[containerId].DeleteAsync();

            // We create a partitioned collection here which needs a partition key. Partitioned collections
            // can be created with very high values of provisioned throughput (up to Throughput = 250,000)
            // and used to store up to 250 GB of data. You can also skip specifying a partition key to create
            // single partition collections that store up to 10 GB of data.
            // For this demo, we create a collection to store SalesOrders. We set the partition key to the account
            // number so that we can retrieve all sales orders for an account efficiently from a single partition,
            // and perform transactions across multiple sales order for a single account number. 
            CosmosContainerSettings containerSettings = new CosmosContainerSettings(containerId, partitionKeyPath: "/AccountNumber");

            // Use the recommended indexing policy which supports range queries/sorting on strings
            containerSettings.IndexingPolicy = new IndexingPolicy(new RangeIndex(DataType.String) { Precision = -1 });

            // Create with a throughput of 1000 RU/s
            container = await database.Containers.CreateContainerIfNotExistsAsync(
                containerSettings,
                throughput: 1000);
        }
    }
}

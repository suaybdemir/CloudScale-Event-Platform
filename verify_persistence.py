import os
from azure.cosmos import CosmosClient, PartitionKey

ENDPOINT = "https://192.168.0.10:8081"
KEY = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
DATABASE_NAME = "EventsDb"
CONTAINER_NAME = "Events"

def verify():
    print(f"🔌 Connecting to Cosmos DB at {ENDPOINT}...")
    try:
        # Disable SSL check for emulator
        client = CosmosClient(ENDPOINT, credential=KEY, connection_verify=False)
        database = client.get_database_client(DATABASE_NAME)
        container = database.get_container_client(CONTAINER_NAME)
        
        # Query
        query = "SELECT VALUE COUNT(1) FROM c WHERE c.EventData.TenantId = 'tenant-loadtest'"
        items = list(container.query_items(query=query, enable_cross_partition_query=True))
        
        count = items[0]
        print(f"✅ Persistence Verification: Found {count} events for 'tenant-loadtest'.")
        
        if count == 0:
            print("❌ WARNING: No events found! Event Processor might be failing.")
    except Exception as e:
        print(f"❌ Error querying Cosmos DB: {e}")

if __name__ == "__main__":
    verify()

from azure.cosmos import CosmosClient, PartitionKey
import os
import urllib3

# Suppress warnings for self-signed certs
urllib3.disable_warnings(urllib3.exceptions.InsecureRequestWarning)

ENDPOINT = "https://localhost:8082"
KEY = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=="
DATABASE_NAME = "EventsDb"
CONTAINER_NAME = "Events"

def init_db():
    print(f"Connecting to {ENDPOINT}...")
    client = CosmosClient(
        ENDPOINT, 
        credential=KEY,
        connection_verify=False,
        connection_mode='Gateway',
        connection_timeout=300
    )
    
    try:
        print("Listing databases...")
        databases = list(client.list_databases())
        if not databases:
            print("❌ No databases found.")
        else:
            print(f"✅ Found {len(databases)} databases:")
            for db in databases:
                print(f" - {db['id']}")
                
    except Exception as e:
        print(f"❌ ERROR: {e}")

if __name__ == "__main__":
    init_db()

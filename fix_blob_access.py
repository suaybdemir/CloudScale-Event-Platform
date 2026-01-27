from azure.storage.blob import BlobServiceClient, PublicAccess

# Azurite Default Connection String
CONN_STR = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://localhost:10000/devstoreaccount1;"

def make_public():
    try:
        print("Connecting to Blob Service...")
        blob_service_client = BlobServiceClient.from_connection_string(CONN_STR, api_version='2021-08-06')
        container_client = blob_service_client.get_container_client("events-archive")

        print("Setting public access policy...")
        container_client.set_container_access_policy(signed_identifiers={}, public_access=PublicAccess.Container)
        
        print("✅ SUCCESS: Container 'events-archive' is now PUBLIC.")
        
    except Exception as e:
        print(f"❌ ERROR: {e}")

if __name__ == "__main__":
    make_public()

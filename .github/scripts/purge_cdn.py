import os
import sys
import json
from tencentcloud.common import credential
from tencentcloud.common.profile.client_profile import ClientProfile
from tencentcloud.common.profile.http_profile import HttpProfile
from tencentcloud.common.exception.tencent_cloud_sdk_exception import TencentCloudSDKException
from tencentcloud.cdn.v20180606 import cdn_client, models

def main():
    try:
        secret_id = os.environ.get("TENCENT_SECRET_ID")
        secret_key = os.environ.get("TENCENT_SECRET_KEY")
        
        # Read paths from environment variable, comma separated
        # Default to https://sfqyweb.xyz/ if not provided, based on user context
        paths_env = os.environ.get("CDN_PATHS")
        if not paths_env:
            paths_env = "https://sfqyweb.xyz/"
            
        paths = []
        for p in paths_env.split(','):
            p = p.strip()
            if p:
                # Ensure directory paths end with / for FlushType 'flush'
                if not p.endswith('/'):
                    p += '/'
                paths.append(p)

        if not secret_id or not secret_key:
            print("Error: TENCENT_SECRET_ID and TENCENT_SECRET_KEY must be set.")
            sys.exit(1)

        print(f"Purging CDN cache for paths: {paths}")

        cred = credential.Credential(secret_id, secret_key)
        httpProfile = HttpProfile()
        httpProfile.endpoint = "cdn.tencentcloudapi.com"

        clientProfile = ClientProfile()
        clientProfile.httpProfile = httpProfile
        client = cdn_client.CdnClient(cred, "ap-guangzhou", clientProfile)

        req = models.PurgePathCacheRequest()
        params = {
            "Paths": paths,
            "FlushType": "flush" # flush means refresh directory
        }
        req.from_json_string(json.dumps(params))

        resp = client.PurgePathCache(req)
        print(resp.to_json_string())

    except TencentCloudSDKException as err:
        print(f"Tencent Cloud SDK Error: {err}")
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()

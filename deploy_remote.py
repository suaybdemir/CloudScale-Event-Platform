import os
import paramiko
import tarfile
from scp import SCPClient
import sys

# Configuration
HOST = "192.168.0.10"
USER = "suayb"
PASS = "suayb"
REMOTE_PATH = "/home/suayb/cloudScale-event-intelligence-platform"
LOCAL_PATH = "/home/arch/cloudScale-event-intelligence-platform"

def create_tarball(output_filename):
    print(f"📦 Packaging project into {output_filename}...")
    with tarfile.open(output_filename, "w:gz") as tar:
        tar.add(LOCAL_PATH, arcname=os.path.basename(LOCAL_PATH), filter=exclude_filter)

def exclude_filter(tarinfo):
    if "node_modules" in tarinfo.name or \
       ".git" in tarinfo.name or \
       "bin" in tarinfo.name or \
       "obj" in tarinfo.name or \
       ".vs" in tarinfo.name or \
       ".idea" in tarinfo.name or \
       "deploy_env" in tarinfo.name:
        return None
    return tarinfo

def run_sudo_command(ssh, command, password):
    """Runs a command with sudo privileges using bash -c."""
    # Escape single quotes in command for bash -c string
    safe_command = command.replace("'", "'\\''")
    full_cmd = f"echo '{password}' | sudo -S bash -c '{safe_command}'"
    
    stdin, stdout, stderr = ssh.exec_command(full_cmd)
    exit_status = stdout.channel.recv_exit_status()
    return exit_status, stdout.read().decode(), stderr.read().decode()

def deploy():
    try:
        print(f"🔌 Connecting to {HOST}...")
        ssh = paramiko.SSHClient()
        ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        ssh.connect(HOST, username=USER, password=PASS)
        
        # 1. Package
        tar_name = "cloudscale_deploy.tar.gz"
        create_tarball(tar_name)
        
        # 2. Upload
        print(f"🚀 Uploading {tar_name} to server...")
        with SCPClient(ssh.get_transport()) as scp:
            scp.put(tar_name, f"/home/{USER}/{tar_name}")
            
        # 3. Extract
        print("📂 Extracting files...")
        ssh.exec_command(f"mkdir -p {REMOTE_PATH}")
        ssh.exec_command(f"tar -xzf /home/{USER}/{tar_name} -C /home/{USER}")
        
        # 4. Run Docker Compose
        print("🛠️  Starting services on remote server...")
        
        # Determine compose command
        compose_cmd = "docker compose"
        stdin, stdout, stderr = ssh.exec_command("docker compose version")
        if stdout.channel.recv_exit_status() != 0:
            compose_cmd = "docker-compose"
        
        # DOWN old - SKIPPED to keep Cosmos warm
        # print(f"   Stopping old containers...")
        # run_sudo_command(ssh, f"cd {REMOTE_PATH} && {compose_cmd} down", PASS)
        
        # UP new
        print(f"   Building and Starting new containers...")
        up_cmd = f"cd {REMOTE_PATH} && {compose_cmd} up -d --build --remove-orphans --force-recreate"
        status, out, err = run_sudo_command(ssh, up_cmd, PASS)
        
        if status != 0:
            print(f"❌ Error starting containers: {err}")
            print(f"   Output: {out}")
        else:
            print("✅ Containers started successfully.")
            print(f"\n🎉 Deployment Completed!")
            print(f"👉 Dashboard: http://{HOST}:5173")
            print(f"👉 API Stats: http://{HOST}:5000/api/dashboard/stats")

        # Cleanup
        os.remove(tar_name)
        ssh.close()

    except Exception as e:
        print(f"\n❌ Deployment FAILED: {str(e)}")

if __name__ == "__main__":
    deploy()

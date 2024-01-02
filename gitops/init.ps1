function CreateCluster {
 
    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, Position = 0)]
        [string]
        $clusterName,
    
        [Parameter(Mandatory = $false, Position = 1)]
        [System.Int32[]]
        $ports
    )

    

    $extraPortMappings = [System.Collections.ArrayList]@()
    ForEach ($port in $ports) {
        $tmp = @{
            "containerPort" = $port
            "hostPort"      = $port
        }
         
        $extraPortMappings.Add($tmp)
    }
     
   
    #kind create cluster --name $clusterName
    $obj = [PsCustomObject]@{
        kind       = 'Cluster'
        apiVersion = 'kind.x-k8s.io/v1alpha4'
        name       = $clusterName
        nodes      = @(
            @{
                role              = 'control-plane'
                extraPortMappings = $extraPortMappings
            },
            @{
                role              = 'worker'
            },
            @{
                role              = 'worker'
            },
            @{
                role              = 'worker'
            }
        ) 
    }   
    $TempFile = New-TemporaryFile
    $yaml = ConvertTo-Yaml $obj | Out-File -FilePath $TempFile
    
    if (kind get clusters | Where-Object { $_ -eq $clusterName }) {
        Write-Host "Cluster $clusterName already exists"
    }
    else{
        kind create cluster --config $TempFile.FullName
    }
    
    kind export kubeconfig --name $clusterName --kubeconfig=.\$clusterName.kubeconfig


    $hostEntry = [PsCustomObject]@{
        apiVersion = 'v1'
        kind       = 'Service'
        metadata   = @{
            name      = $clusterName 
            namespace = 'vslive'
        }
        spec       = @{
            type         = 'ExternalName'
            externalName = 'host.docker.internal'
        }
    }
    $yaml = ConvertTo-Yaml $hostEntry | Out-File -FilePath .\$clusterName-host.yaml
    New-Item -Path .\$clusterName -ItemType Directory -Force
    New-Item -Path .\$clusterName\.gitkeep -ItemType File -Force
    $kubeconfig = [System.IO.FileInfo] "$PWD\$clusterName.kubeconfig"
    SetupFlux $clusterName $kubeconfig
}

function SetupFlux {

    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, Position = 0)]
        [string]
        $clusterName,
    
        [Parameter(Mandatory = $true, Position = 1)]
        [System.IO.FileInfo]
        $kubeconfig
    )
    Write-Host $kubeconfig
    #  we will install flux on the cluster using the kubeconfig passed
    kubectl apply -f https://github.com/fluxcd/flux2/releases/download/v0.31.5/install.yaml --kubeconfig $kubeconfig.FullName

    $common = @{
        apiVersion = 'source.toolkit.fluxcd.io/v1beta2'
        kind       = 'GitRepository'
        metadata   =
        @{ 
            name      = 'common'
            namespace = 'flux-system'
        }
        spec       = @{
            interval = '1m0s'
            url      = 'https://github.com/ewassef/skupper-dapr-ohmy'
            ref      = @{branch = 'main' }
        }
    }

    $TempFile = New-TemporaryFile
    ConvertTo-Yaml $common | Out-File -FilePath $TempFile
    kubectl apply -f $TempFile.FullName --kubeconfig $kubeconfig.FullName
    Remove-item $TempFile.FullName


    $common = @{
        apiVersion = 'kustomize.toolkit.fluxcd.io/v1beta2'
        kind       = 'Kustomization'
        metadata   = @{ 
            name      = 'common'
            namespace = 'flux-system'
        } 
        spec       = @{
            interval  = '1m0s'
            sourceRef = @{
                kind = 'GitRepository'
                name = 'common'
            }
            path      = './gitops/common'
            prune     = $true
        }
    } 

    $TempFile = New-TemporaryFile
    ConvertTo-Yaml $common | Out-File -FilePath $TempFile
    kubectl apply -f $TempFile.FullName --kubeconfig $kubeconfig.FullName
    Remove-item $TempFile.FullName


    $common = @{
        apiVersion = 'kustomize.toolkit.fluxcd.io/v1beta2'
        kind       = 'Kustomization'
        metadata   = @{ 
            name      = $clusterName
            namespace = 'flux-system'
        } 
        spec       = @{
            interval  = '1m0s'
            sourceRef = @{
                kind = 'GitRepository'
                name = 'common'
            }
            path      = './gitops/$($clusterName)'
            prune     = $true
        }
    } 

    $TempFile = New-TemporaryFile
    ConvertTo-Yaml $common | Out-File -FilePath $TempFile
    kubectl apply -f $TempFile.FullName --kubeconfig $kubeconfig.FullName
    Remove-item $TempFile.FullName
}

# CreateCluster testcluster
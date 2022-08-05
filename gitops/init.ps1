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
            }
        ) 
    }   
    $TempFile = New-TemporaryFile
    $yaml = ConvertTo-Yaml $obj | Out-File -FilePath $TempFile
    
    
    kind create cluster --config $TempFile.FullName
    kind export kubeconfig --name $clusterName --kubeconfig=.\$clusterName.kubeconfig


    $hostEntry = [PsCustomObject]@{
        apiVersion = 'v1'
        kind       = 'Service'
        metadata   = @{name = $clusterName }
        spec       = @{
            type         = 'ExternalName'
            externalName = 'host.docker.internal'
        }
    }
    $yaml = ConvertTo-Yaml $hostEntry | Out-File -FilePath .\gitops\common\$clusterName-host.yaml
}


# CreateCluster testcluster
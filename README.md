# Welcome to the cluster networking demo repo!

There are two similar but seperate demos here in this repo:

1- K8S operator to automate [Skupper](https://skupper.io) networking between clusters - with all the fixins

2- Step by step process for exposing [Dapr](https://dapr.io) service invocation over the Skupper VAN

---

## K8S operator sample

To run this sample ensure you have the following installed

- Docker
- KinD (Kubernetes in Docker)
- Powershell Core

Steps:

1. Stand up a Vault docker image to act as our shared secret store:

- Run the `.\gitops\create_vault.ps1` script to create the image with a weak default password

2. Create at least 2 clusters

- Run the `.\gitops\init.ps1` to register the two function calls in your session.
- To create a `public` cluster (i.e. a cluster that can be reached externally somehow) we will create a Cluster and add a `NodePort` to a pre-determined port mapped to the docker image.
- _Note: in this example, we are using ports `30080` and `30081` but you can use whatever you have available as long as you pass them into your `ClusterPool` CRD later on_
- run `createcluster publiccluster @(30080,30081)`
- Next create a private cluster (i.e. one that has no external access)
- run `createcluster privatecluster`
- Now you will have 2 clusters with FluxCD installed pointing to this repo.
- Once all is complete, connect to these clusters and run `kubectl get clusterpools -A`. The result should look like this:

```bash
NAMESPACE   NAME     CONNECTED CLUSTERS   LOCAL EXPOSED SERVICES   TOTAL EXPOSED SERVICES   STATE        HAS EXTERNAL ACCESS
vslive      vslive   1                    0                        0                        Registered   true
```

- Verify you have 2 clusters connected.
- run `createcluster privatecluster2`
- Once complete, rerun the above `kubectl command` and verify there are 3 clusters connected.

3. Tear down:

- Run `kind delete cluster --name=publiccluster;kind delete cluster --name=privatecluster;kind delete cluster --name=privatecluster2;` to reset your machine state

---

## Dapr integration with Skupper

-- coming soon

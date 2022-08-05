#https://skupper.io/docs/declarative/tutorial.html

kind create cluster --config=cluster1.kind.yaml
kind create cluster --config=cluster2.kind.yaml



kind export kubeconfig --name cluster1 --kubeconfig=.\cluster1.kubeconfig
kind export kubeconfig --name cluster2 --kubeconfig=.\cluster2.kubeconfig

kubectl apply -f .\cluster1.yaml --kubeconfig=cluster1.kubeconfig
kubectl apply -f .\cluster2.yaml --kubeconfig=cluster2.kubeconfig

kubectl apply -f .\hostEntries.yaml --kubeconfig=cluster1.kubeconfig
kubectl apply -f .\hostEntries.yaml --kubeconfig=cluster2.kubeconfig


kubectl apply -f .\deploy-watch-all-ns.yaml --kubeconfig=cluster1.kubeconfig
kubectl apply -f .\deploy-watch-all-ns.yaml --kubeconfig=cluster2.kubeconfig

kubectl rollout status deploy/skupper-router --kubeconfig=cluster2.kubeconfig
#patch the service using the file
kubectl patch service skupper-router --patch-file .\patch-skupper-router-svc-cluster2.json --kubeconfig=cluster2.kubeconfig

#Submit a token request to cluster2 for cluster 1
kubectl apply -f .\token-request.yaml --kubeconfig=cluster2.kubeconfig

#export the new join token from cluster2
kubectl get secret -o yaml cluster1-secret --kubeconfig=cluster2.kubeconfig > .\join-token.yaml
#import the token to cluster1 using a replace 
kubectl apply -f .\join-token.yaml --kubeconfig=cluster1.kubeconfig


# At this point we are connected, lets install dapr
helm repo update
helm upgrade --install dapr dapr/dapr --create-namespace --kubeconfig=cluster1.kubeconfig
helm upgrade --install dapr dapr/dapr --create-namespace --kubeconfig=cluster2.kubeconfig
kubectl patch configurations/daprsystem --patch-file patch-dapr-config.json --kubeconfig=cluster1.kubeconfig
kubectl patch configurations/daprsystem --patch-file .\patch-dapr-config.json --kubeconfig=cluster2.kubeconfig
# clone the quickstarts .. dont worry, its in the .gitignore file
git clone https://github.com/dapr/quickstarts.git
 

# install the backend in cluster 1
helm install redis bitnami/redis --kubeconfig=cluster1.kubeconfig
kubectl apply -f .\quickstarts\tutorials\hello-kubernetes\deploy\redis.yaml --kubeconfig=cluster1.kubeconfig
kubectl apply -f .\quickstarts\tutorials\hello-kubernetes\deploy\node.yaml --kubeconfig=cluster1.kubeconfig
kubectl annotate deploy/nodeapp dapr.io/sidecar-listen-addresses="0.0.0.0" --kubeconfig=cluster1.kubeconfig

# install the frontend in cluster 2
kubectl apply -f .\quickstarts\tutorials\hello-kubernetes\deploy\python.yaml --kubeconfig=cluster2.kubeconfig

kubectl rollout status deploy/nodeapp --kubeconfig=cluster1.kubeconfig
kubectl rollout status deploy/pythonapp --kubeconfig=cluster2.kubeconfig

#expose the services to each other
 
kubectl annotate service/nodeapp-dapr skupper.io/proxy="tcp" --kubeconfig=cluster1.kubeconfig --overwrite=true
kubectl annotate service/nodeapp skupper.io/proxy="tcp" --kubeconfig=cluster1.kubeconfig  --overwrite=true

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

#patch the service using the file
kubectl patch service skupper-router --patch-file .\patch-skupper-router-svc-cluster2.json --kubeconfig=cluster2.kubeconfig

#Submit a token request to cluster2 for cluster 1
kubectl apply -f .\token-request.yaml --kubeconfig=cluster2.kubeconfig

#export the new join token from cluster2
kubectl get secret -o yaml cluster1-secret --kubeconfig=cluster2.kubeconfig > .\join-token.yaml
#import the token to cluster1 using a replace 
kubectl apply -f .\join-token.yaml --kubeconfig=cluster1.kubeconfig
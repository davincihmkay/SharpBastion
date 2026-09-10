namespace SharpBastion.ClientRequestObject;

[Serializable]
public class RepomixCliClientPackageRepositoryRequestObject
{
        public string repositoryName { get; set; }
        
        public RepomixCliClientPackageRepositoryRequestObject(string repositoryName)
        {
            this.repositoryName = repositoryName;
        }
    
}
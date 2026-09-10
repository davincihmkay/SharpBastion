using SharpBastion.Domain;
using SharpBastion.ValueObjects;
using File = SharpBastion.Domain.File;

namespace SharpBastion.PromptBuilder;

public static class RepositoryPromptBuilder
{
    public static List<Message> BuildIngestionMessages(Repository repository)
    {
        var messages = new List<Message>
        {
            new($"I will send you messages now, describing the following repository and their file contents. I will ask questions about this repository after the messages have been finished. \n Repository Name: {repository.Name}")
        };

        if (repository.TryGetXmlFileName(out var xmlFileName))
        {
            messages.Add(new Message(
                $"The repository has been packaged into a repomix file named '{xmlFileName}' " +
                $"located in your working directory. Read this file to understand the full " +
                $"repository structure and contents before answering questions."));
        }
        else
        {
            messages.AddRange(BuildFolderMessages(repository.RootFolder));
        }

        return messages;
    }

    private static List<Message> BuildFolderMessages(Folder folder)
    {
        var folderMessage = new Message("Folder Name: " + folder.Name + Environment.NewLine + Environment.NewLine
                            + string.Join(Environment.NewLine, folder.SubFolders.Select(x => "- " + x.Name + Environment.NewLine)));

        var messages = folder.Files.Select(BuildFileMessage).ToList();
        var messagesSubFolders = folder.SubFolders.SelectMany(BuildFolderMessages).ToList();

        messages.Insert(0, folderMessage);
        messages.AddRange(messagesSubFolders);

        return messages;
    }

    private static Message BuildFileMessage(File file)
    {
        var fileNameHeader = "File name: " + file.Name + "\n\n";
        return string.IsNullOrEmpty(file.Content)
            ? new Message(fileNameHeader + "No Lines found, either file was too big or had no content")
            : new Message(fileNameHeader + file.Content);
    }
}   
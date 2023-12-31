using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.IO;
using System;

/// <summary>
/// Communicates with GPT and illicits text responses from given prompts.
/// </summary>
public class GptCommunicator : MonoBehaviour
{
    [System.Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    [System.Serializable]
    public class RequestBody
    {
        public string model;
        public Message[] messages;
        public double temperature;
        public double presence_penalty;
        public double frequency_penalty;
    }

    [System.Serializable]
    public class Choice
    {
        public int index;
        public Message message;
        public string finish_reason;
    }

    [System.Serializable]
    public class Usage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [System.Serializable]
    public class Response
    {
        public string id;
        public string object_name;
        public int created;
        public string model;
        public Choice[] choices;
        public Usage usage;
    }

    public class QueuePrompt
    {
        string prompt;
        Personality caller;
        ResponseReceived callback;
    }

    [Tooltip("Enter your OpenAI API key here.")]
    [SerializeField] private string mApiKey = "YOUR API KEY HERE";

    [Tooltip("Open AI competions URL. Probably shouldn't be changed.")]
    [SerializeField] private string mUrl = 
        "https://api.openai.com/v1/chat/completions";

    [Tooltip("Open AI model to generate responses from.")]
    [SerializeField] private string mModel = "gpt-4";

    [Tooltip("How often in seconds to make a request of the Open AI API.")]
    [SerializeField] float mRateLimit = 3.0f;

    [Tooltip("Turns GPT response requests on/off for debugging.")]
    [SerializeField] bool mSendRequests = true;

    [Tooltip("Records output for analysis or training.")]
    [SerializeField] private bool mRecordOutput = false;

    /// <summary>
    /// Current filename to record output to.
    /// </summary>
    private string mOutFile;

    /// <summary>
    /// Current path to record output to.
    /// </summary>
    private string mOutPath;

    /// <summary>
    /// Writes output to file.
    /// </summary>
    private StreamWriter mWriter;

    /// <summary>
    /// Builds strings for output.
    /// </summary>
    private StringBuilder mBuilder;

    /// <summary>
    /// Time of last request made of Open AI API.
    /// </summary>
    private float mLastRequestTime = 0;

    /// <summary>
    /// Delegate for callbacks to be executed upon receipt of a response from 
    /// Open AI API.
    /// </summary>
    /// <param name="received">
    /// Text response generated by Open AI model.
    /// </param>
    public delegate void ResponseReceived(string received);

    /// <summary>
    /// Delegate for functions called when denial string is received.
    /// </summary>
    public delegate void Denial(Personality personality);

    /// <summary>
    /// Event fired when denial string is received.
    /// </summary>
    public event Denial OnDenial;

    /// <summary>
    /// Delegate for functions called when data is recorded.
    /// </summary>
    public delegate void Record(Personality personality);

    /// <summary>
    /// Event fired when data is recorded.
    /// </summary>
    public event Record OnRecord;

    /// <summary>
    /// Called on the frame when a script is enabled just before any of the
    /// Update methods are called the first time.
    /// </summary>
    private void OnEnable()
    {
        if (mRecordOutput)
        {
            mBuilder = new StringBuilder(Defines.MAX_TOKENS);
            mOutFile = $"{Defines.OUT_PREF}_" +
                $"{DateTime.Now.ToString(Defines.DATE_FORMAT)}.txt";
            mOutPath = Path.Combine(Application.persistentDataPath, mOutFile);
            mWriter = new StreamWriter(mOutPath, append: true);
            Debug.Log("Opened file for recording data at: " + mOutPath);
        }
    }

    private void OnDisable()
    {
        if (mWriter != null)
            mWriter.Close();
    }

    /// <summary>
    /// Requests a statement to be made by Character from GPT in reply to 
    /// an ongoing conversation.
    /// </summary>
    /// <param name="prompt">
    /// Conversation GPT should generate a reply to.
    /// </param>
    /// <param name="callback">
    /// Method to execute once reply has been received from GPT.
    /// </param>
    public void RequestConversationalReply
        (string prompt, Personality caller, ResponseReceived callback)
    {
        string replyPrompt = $"{prompt} {Defines.REPLY_INSTRUCT}" +
            $"{Defines.RESPONSE_CHECK}{Defines.RESPONSE_DENY}";
        StartCoroutine(PromptGpt(replyPrompt, caller, callback, prompt));
    }

    public void RequestVisualQueuePrompt
        (string prompt, Personality caller, ResponseReceived callback)
    {
        string replyPrompt = $"{Defines.VIS_ASSESS_HEAD} {prompt}" +
            $" {Defines.VIS_ASSESS_SAY} {Defines.RESPONSE_CHECK}" +
            $"{Defines.RESPONSE_DENY}";
        StartCoroutine(PromptGpt(replyPrompt, caller, callback, prompt));
    }

    /// <summary>
    /// Requests instructions on what to do from GPT based on a text 
    /// description of a Character's current state in the game.
    /// </summary>
    /// <param name="prompt">
    /// Description of character's current state.
    /// </param>
    /// <param name="callback">
    /// Method to execute upon receipt of instructions from GPT.
    /// </param>
    public void RequestReactionInstructions
        (string prompt, Personality caller, ResponseReceived callback)
    {
        string spokenReplyPrompt = $"{prompt} {Defines.REACT_INSTRUCT}";
        StartCoroutine(PromptGpt(spokenReplyPrompt, caller, callback, prompt));
    }

    /// <summary>
    /// Prompts Open AI GPT model.
    /// </summary>
    /// <param name="prompt">
    /// Text prompt for GPT.
    /// </param>
    /// <param name="callback">
    /// Method to execute upon receipt of response from GPT.
    /// </param>
    /// <returns></returns>
    IEnumerator PromptGpt(
        string prompt, Personality caller, 
        ResponseReceived callback, string original)
    {
        if (caller.Verbose)
            Debug.Log($"Sending reply request for prompt:\n{prompt} with" +
                $" key {mApiKey}.");
        if (mSendRequests)
        {
            // Current game time.
            float time = Time.realtimeSinceStartup;

            // Difference between current time and the last  request.
            float delta = time - mLastRequestTime;

            // How long to wait before making another request.
            float waitSeconds = mLastRequestTime == 0 || delta >= mRateLimit ?
                0 : mRateLimit - delta;

            yield return new WaitForSeconds(waitSeconds);
            UnityWebRequest www = new UnityWebRequest(mUrl, "POST");
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Authorization", $"Bearer {mApiKey}");
            Message message = new Message
            {
                role = "user",
                content = prompt
            };
            caller.Messages.Add(message);
            RequestBody body = new RequestBody
            {
                model = mModel,
                messages = caller.Messages.ToArray(),
                temperature = caller.Temperature,
                presence_penalty = caller.PresencePenalty,
                frequency_penalty = caller.FrequencyPenalty
            };
            string bodyJson = JsonUtility.ToJson(body);
            byte[] bodyRaw = new System.Text.UTF8Encoding().GetBytes(bodyJson);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            DownloadHandlerBuffer dH = new DownloadHandlerBuffer();
            www.downloadHandler = dH;
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                Response response =
                    JsonUtility.FromJson<Response>(www.downloadHandler.text);
                string responseText = response.choices[0].message.content;
                if (!responseText.Contains(Defines.RESPONSE_DENY))
                {
                    caller.Messages.Add(
                        new Message { role = "assistant", content = responseText }
                    );
                    callback?.Invoke(responseText.Replace("\"", string.Empty));

                    if (mRecordOutput)
                        RecordOutput(caller, original, responseText);
                }
                else
                {
                    Debug.Log($"Got denial string \"{Defines.RESPONSE_DENY}\"" +
                        $" from prompt:\n\"{prompt}\"");
                    OnDenial?.Invoke(caller);
                }
            }
            else
            {
                Debug.Log($"Requester Error: {www.error}");
            }
            mLastRequestTime = Time.realtimeSinceStartup;
        }
    }

    /// <summary>
    /// Records input to gpt and output from gpt to file.
    /// </summary>
    /// <param name="personality">
    /// Personality being served by gpt.
    /// </param>
    /// <param name="prompt">
    /// Dialogue prompt being sent to gpt.
    /// </param>
    /// <param name="output">
    /// Output received from GPT.
    /// </param>
    private void RecordOutput(Personality personality, string prompt, string output)
    {
        // First charcter of prompt, capitalized.
        string promptUpper = $"{prompt[0]}".ToUpper();

        // Prompt formatted with first character capitalized.
        string promptFormatted = $"{promptUpper[0]}{prompt[1..]}";

        // String to record to file.
        string record = $"You are {personality.BackStory}." +
            $" {personality.Summary}\n{promptFormatted} What is your" +
            $" response?\n{output}";

        mWriter.WriteLine(record);
        OnRecord?.Invoke(personality);
    }
}

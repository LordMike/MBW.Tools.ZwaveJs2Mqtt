namespace ZwaveMqttTemplater.Z2M
{
    class Z2MApiCallResult<T>
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public T Result { get; set; }
    }
}

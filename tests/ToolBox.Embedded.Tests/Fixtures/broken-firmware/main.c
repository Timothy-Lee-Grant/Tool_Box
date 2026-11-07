/* Deliberately broken — BuildToolsTests asserts build_firmware() reports
 * failure and get_build_log() surfaces the real compiler error below. */
int main(void)
{
    return undefined_symbol_that_does_not_exist;
}

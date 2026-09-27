import { createApp } from "vue";
import App from "./App.vue";
import "./tailwind.css"
import "./fonts.css"
import Antd from 'ant-design-vue';
import 'ant-design-vue/dist/reset.css';
import router from "./utils/router";
import i18n from "./utils/i18n";
import { installGlobalErrorHandlers } from "./utils/globalErrorHandler";
import { pushLocalError } from "./stores/errorStore";

// 全局错误捕获要在挂载之前安装，才能覆盖启动阶段的异常。
installGlobalErrorHandlers();

const app = createApp(App);

/**
 * Vue 组件内部抛出的异常交给统一的错误存储，
 * 这样「程序错误提醒」能看到它们，而不是只在控制台一闪而过。
 */
app.config.errorHandler = (err, _instance, info) => {
    const error = err instanceof Error ? err : new Error(String(err));
    pushLocalError(error.message || '组件渲染出错', {
        detail: `${info}\n${error.stack ?? ''}`.trim(),
        source: 'vue'
    });
};

app.use(router)
app.use(Antd)
app.use(i18n)
app.mount("#app");

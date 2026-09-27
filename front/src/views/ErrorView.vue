<template>
    <div class="tw:h-screen tw:w-full tw:flex tw:flex-col tw:justify-center tw:items-center tw:bg-gradient-to-br tw:from-gray-50 tw:to-gray-100">
        <div class="tw:w-32 tw:h-32 tw:mb-8">
            <svg class="w-32 h-32 mb-4 tw:transition-all tw:duration-300 tw:hover:scale-110" viewBox="0 0 120 120">
                <circle cx="60" cy="60" r="50" fill="#ef4444" class="tw:opacity-80" />
                <path d="M40,40 L80,80 M80,40 L40,80" stroke="white" stroke-width="10" stroke-linecap="round" class="tw:opacity-90" />
            </svg>
        </div>
        <h1 class="tw:text-3xl tw:font-bold tw:text-center tw:mb-6 tw:text-red-500">错误</h1>
        <div class="tw:w-1/2 tw:max-w-md tw:bg-white tw:p-8 tw:rounded-xl tw:shadow-xl tw:border tw:border-gray-100 tw:transition-all tw:duration-300 tw:hover:shadow-2xl">
            <div class="tw:text-center tw:mb-6">
                <p class="tw:text-lg tw:font-medium tw:text-gray-700 mb-2">
                    {{ errorMessage }}
                </p>
                <div v-if="errorCode" class="tw:inline-block tw:px-4 tw:py-1 tw:bg-red-50 tw:text-red-600 tw:rounded-full tw:text-sm tw:font-medium mt-2">
                    错误码：{{ errorCode }}
                </div>
            </div>
            <div v-if="suggestions.length > 0" class="tw:mt-6 tw:bg-gray-50 tw:p-4 tw:rounded-lg">
                <p class="tw:text-sm tw:font-medium tw:text-gray-700 mb-3">建议解决方案：</p>
                <ul class="tw:text-sm tw:text-gray-600">
                    <li v-for="(suggestion, index) in suggestions" :key="index" class="tw:mb-2 tw:flex tw:items-start">
                        <span class="tw:w-5 tw:h-5 tw:flex tw:items-center tw:justify-center tw:bg-red-100 tw:text-red-500 tw:rounded-full tw:mr-3 tw:mt-0.5">
                            {{ Number(index) + 1 }}
                        </span>
                        <span>{{ suggestion }}</span>
                    </li>
                </ul>
            </div>
            <div class="tw:mt-8 tw:flex tw:justify-center">
                <button 
                    class="tw:px-6 tw:py-2.5 tw:bg-[#67eac3] tw:text-gray-800 tw:rounded-lg tw:hover:bg-[#56d9b0] tw:transition-all tw:duration-300 tw:font-medium tw:shadow-sm tw:hover:shadow"
                    @click="goBack"
                >
                    返回首页
                </button>
            </div>
        </div>
    </div>
</template>

<script lang="ts" setup>
import { useRoute, useRouter } from 'vue-router';
import { ErrorCode, getErrorSuggestions } from '../utils/errorCodes';

const route = useRoute();
const router = useRouter();
const errorReason = route.query.e as string;
const errorCodeStr = route.query.code as string;
const errorCode = errorCodeStr ? parseInt(errorCodeStr) as ErrorCode : undefined;
const errorMessage = errorReason ? errorReason : 'DeEarthX.Core 启动失败！';
const suggestions = errorCode ? getErrorSuggestions(errorCode) : [];

function goBack() {
    router.push('/');
}
</script>
